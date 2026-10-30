# -*- coding: utf-8 -*-
"""Build a Player through the live Unity Pipeline server.

Chain: restore-check -> l0-dep-scan -> buildHash check -> profile sync ->
builtin bundles -> dry-run -> build -> build_status poll -> artifact/freshness.
"""

import argparse
import json
import re
import shutil
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402


# ---- 平台映射（profile → Unity 构建参数；纯函数，供 scripts/tests 契约测试） ----

# profile.platform → 默认 buildTargetGroup（profile 显式 buildTargetGroup 优先）。
PLATFORM_TARGET_GROUPS = {
    "StandaloneWindows64": "Standalone",
    "Android": "Android",
}

# profile.platform → Player 产物扩展名。
PLATFORM_PLAYER_EXTENSIONS = {
    "StandaloneWindows64": ".exe",
    "Android": ".apk",
}

# 内置内容包（YooAsset Offline）已接线的平台；新平台先接线 BuiltinBundleBuildInvoker 再登记。
BUNDLE_PLATFORMS = ("StandaloneWindows64", "Android")


def named_build_target(profile):
    group = profile.get("buildTargetGroup") or PLATFORM_TARGET_GROUPS.get(profile.get("platform") or "")
    if group not in ("Standalone", "Android"):
        raise RuntimeError(f"profile buildTargetGroup unsupported: {profile.get('buildTargetGroup')!r}")
    return group


def player_extension(profile):
    platform = profile.get("platform") or ""
    if platform not in PLATFORM_PLAYER_EXTENSIONS:
        raise RuntimeError(f"player extension unknown for platform: {platform!r}")
    return PLATFORM_PLAYER_EXTENSIONS[platform]


def artifact_glob(profile):
    return profile.get("artifactGlob") or "*" + player_extension(profile)


def bundles_supported(profile):
    return (profile.get("platform") or "") in BUNDLE_PLATFORMS


def backend_switch_cs(profile):
    target = named_build_target(profile)
    implementation = "Mono" if str(profile.get("backend") or "").lower() == "mono" else "IL2CPP"
    return (
        f"UnityEditor.Build.NamedBuildTarget t = UnityEditor.Build.NamedBuildTarget.{target};\n"
        f"UnityEditor.PlayerSettings.SetScriptingBackend(t, UnityEditor.ScriptingImplementation.{implementation});\n"
        "return UnityEditor.PlayerSettings.GetScriptingBackend(t).ToString();\n"
    )


def build_output_expr(profile):
    extension = player_extension(profile)
    return (
        'System.IO.Path.Combine("' + str(profile.get("output")) + '", '
        "UnityEditor.PlayerSettings.productName + \"" + extension + "\")"
    )


def bundles_cs(profile):
    platform = profile.get("platform") or ""
    return (
        "public static class BuildBundles\n"
        "{\n"
        "    public static string Start()\n"
        "    {\n"
        f"        LiteGame.EditorTools.BuiltinBundleBuildInvoker.RunAsync(UnityEditor.BuildTarget.{platform});\n"
        "        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status;\n"
        "    }\n"
        "\n"
        "    public static string Status()\n"
        "    {\n"
        "        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status\n"
        "             + \" | out=\" + LiteGame.EditorTools.BuiltinBundleBuildInvoker.LastOutputDirectory;\n"
        "    }\n"
        "}\n"
    )


# ---- Android 明文（cleartext）通道策略（纯函数，供 L1 单测） ----

# 自定义主清单（useCustomMainManifest=1 时打包消费的文件）。
ANDROID_MANIFEST = Path("Assets/Plugins/Android/AndroidManifest.xml")

# 明文放行属性：只有 development 通道（开发/封闭测试）才注入。
CLEARTEXT_ATTRIBUTE = 'android:usesCleartextTraffic="true"'

# 允许明文放行的通道；其余通道一律要求清单干净——明文泄漏必须让出包当场失败。
CLEARTEXT_CHANNELS = ("development",)


def channel_allows_cleartext(profile):
    return (profile.get("channel") or "") in CLEARTEXT_CHANNELS


def _strip_xml_comments(text):
    """去掉 XML 注释再判属性（注释里提到属性名不等于清单真放开了明文）。"""
    out = []
    index = 0
    while True:
        start = text.find("<!--", index)
        if start < 0:
            out.append(text[index:])
            break
        out.append(text[index:start])
        end = text.find("-->", start + 4)
        if end < 0:
            break
        index = end + 3
    return "".join(out)


def manifest_allows_cleartext(text):
    return CLEARTEXT_ATTRIBUTE in _strip_xml_comments(text or "")


def apply_cleartext(text, allow):
    """按通道改写主清单的 <application> 起始标签（幂等；缺该标签即抛）。"""
    if manifest_allows_cleartext(text) == bool(allow):
        return text
    if allow:
        index = text.find("<application")
        if index < 0:
            raise RuntimeError("android manifest has no <application> tag - cannot apply cleartext")
        end = index + len("<application")
        return text[:end] + " " + CLEARTEXT_ATTRIBUTE + text[end:]
    return text.replace(" " + CLEARTEXT_ATTRIBUTE, "")


def assert_cleartext_absent(text):
    if manifest_allows_cleartext(text):
        raise RuntimeError(
            "release channel forbids cleartext in the Android manifest - "
            "a previous development build left " + CLEARTEXT_ATTRIBUTE + " behind"
        )


def sync_android_cleartext(project, profile):
    """按通道同步主清单；返回被改动文件的原始全文（未改动＝None，供 finally 还原）。"""
    path = project / ANDROID_MANIFEST
    if not path.is_file():
        raise RuntimeError(f"android manifest missing: {ANDROID_MANIFEST}")
    original = path.read_text(encoding="utf-8")
    if not channel_allows_cleartext(profile):
        assert_cleartext_absent(original)
        return None
    patched = apply_cleartext(original, True)
    if patched == original:
        return None
    path.write_text(patched, encoding="utf-8", newline="\n")
    info("build/player", f"android manifest: cleartext ON (channel={profile.get('channel')})")
    return original


def restore_android_cleartext(project, original):
    if original is None:
        return
    (project / ANDROID_MANIFEST).write_text(original, encoding="utf-8", newline="\n")
    info("build/player", "android manifest: cleartext attribute restored (source stays release-safe)")


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Build a Player through Unity Pipeline")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--profile", default="BuildProfiles/windows-x64.json")
    parser.add_argument("--skip-restore-check", action="store_true")
    parser.add_argument("--skip-bundles", action="store_true")
    return parser.parse_args(argv)


class Builder:
    def __init__(self, project):
        self.project = project

    def pipeline(self, command, args=None):
        unity = shutil.which("unity")
        if not unity:
            raise RuntimeError("unity CLI not found on PATH")
        cmd = [unity, "command", command]
        cmd.extend(args or [])
        cmd.extend(["--project-path", str(self.project), "--proxy-disable"])
        result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        output = (result.stdout or "") + (result.stderr or "")
        if result.returncode != 0:
            raise RuntimeError(f"pipeline '{command}' failed (exit {result.returncode}): {output.strip()}")
        for line in output.splitlines():
            parts = line.split("\t")
            if len(parts) >= 3 and parts[2].startswith("{"):
                return parts[2]
        return output

    def eval_file(self, name, code):
        path = self.project / "Temp" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(code.encode("utf-8"))
        return self.pipeline("eval_file", ["--file", f"Temp/{name}"])

    def run_gate(self, script, args):
        result = subprocess.run([sys.executable, str(self.project / "scripts" / script), *args], cwd=str(self.project))
        if result.returncode != 0:
            raise RuntimeError(f"{script} failed with exit code {result.returncode}")


def load_json(text):
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return {}


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
    profile_path = Path(args.profile)
    if not profile_path.is_absolute():
        profile_path = project / profile_path
    if not profile_path.is_file():
        return fail("build/player", f"profile missing: {profile_path}")
    try:
        profile = json.loads(profile_path.read_text(encoding="utf-8"))
    except Exception as exc:
        return fail("build/player", f"profile parse failed: {exc}")

    builder = Builder(project)
    manifest_original = None
    try:
        if not args.skip_restore_check:
            info("build/player", "== restore-check ==")
            builder.run_gate("gate/restore-check.py", ["--project-path", str(project)])
            info("build/player", "== l0-dep-scan ==")
            builder.run_gate("gate/l0-dep-scan.py", ["--project-path", str(project)])

        info("build/player", "== buildhash check ==")
        builder.run_gate("codegen/gen-build-hash.py", ["--check-shared"])

        info(
            "build/player",
            f"BuildProfile: {profile.get('profileName')} | platform={profile.get('platform')} "
            f"backend={profile.get('backend')} scenes={len(profile.get('scenes') or [])}",
        )

        build_settings = load_json(builder.pipeline("get_build_settings"))
        if build_settings.get("activeBuildTarget") != profile.get("platform"):
            info("build/player", f"switching build target -> {profile.get('platform')}")
            builder.pipeline("switch_build_target", ["--target", profile["platform"]])
            time.sleep(10)

        player_settings = load_json(builder.pipeline("get_player_settings"))
        if ((player_settings.get("values") or {}).get("scriptingBackend")) != profile.get("backend"):
            info("build/player", f"switching scripting backend -> {profile.get('backend')}")
            builder.eval_file("build-backend-switch.cs", backend_switch_cs(profile))
            time.sleep(25)
            player_settings = load_json(builder.pipeline("get_player_settings"))
            if ((player_settings.get("values") or {}).get("scriptingBackend")) != profile.get("backend"):
                raise RuntimeError("backend switch did not persist")

        build_settings = load_json(builder.pipeline("get_build_settings"))
        existing = {scene.get("path") for scene in build_settings.get("scenes") or []}
        for scene in profile.get("scenes") or []:
            if scene not in existing:
                info("build/player", f"adding scene to build: {scene}")
                builder.pipeline("add_scene_to_build", ["--path", scene])
        profile_scenes = set(profile.get("scenes") or [])
        build_settings = load_json(builder.pipeline("get_build_settings"))
        for scene in build_settings.get("scenes") or []:
            path = scene.get("path")
            if path not in profile_scenes:
                info("build/player", f"removing scene from build (not in profile): {path}")
                builder.pipeline("remove_scene_from_build", ["--path", path])

        deploy_args = project / "Assets" / "StreamingAssets" / "deploy.args"
        endpoints = profile.get("endpoints") or {}
        if endpoints:
            lines = []
            for key, value in endpoints.items():
                value = "" if value is None else str(value)
                if not value.strip():
                    continue
                if re.search(r"\s", value):
                    raise RuntimeError(f"endpoint '{key}' has whitespace - refuse to bake (token form)")
                lines.append(f"-{key}={value}")
            if lines:
                info("build/player", f"baking deploy.args ({len(endpoints)} endpoint(s))")
                text = "\n".join(lines)
                builder.eval_file(
                    "bake-deploy-args.cs",
                    'string abs = System.IO.Path.GetFullPath("Assets/StreamingAssets/deploy.args");\n'
                    "System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(abs));\n"
                    f'System.IO.File.WriteAllText(abs, @"{text}");\n'
                    'UnityEditor.AssetDatabase.ImportAsset("Assets/StreamingAssets/deploy.args");\n'
                    'return "baked: " + abs;',
                )
        elif deploy_args.exists():
            info("build/player", "endpoints empty/absent -> deleting stale deploy.args")
            builder.eval_file(
                "bake-deploy-args-delete.cs",
                'string rel = "Assets/StreamingAssets/deploy.args";\n'
                'string abs = System.IO.Path.GetFullPath(rel);\n'
                'if (System.IO.File.Exists(abs)) { System.IO.File.Delete(abs); UnityEditor.AssetDatabase.Refresh(); }\n'
                'return "deleted (or absent): " + abs;',
            )

        if args.skip_bundles:
            info("build/player", "== bundles: SKIPPED (--skip-bundles; packaged content may be stale) ==")
        elif not bundles_supported(profile):
            raise RuntimeError(
                f"profile platform is {profile.get('platform')} - builtin bundles not wired for it; "
                "use --skip-bundles to acknowledge"
            )
        else:
            info("build/player", "== bundles: build builtin content bundles (Offline) ==")
            builder.eval_file("build-bundles.cs", bundles_cs(profile))
            builder.pipeline("run_script", ["--file", "Temp/build-bundles.cs", "--entry", "BuildBundles.Start"])
            bundle_state = ""
            polls = 0
            while polls * 10 < 1200:
                time.sleep(10)
                polls += 1
                bundle_state = builder.pipeline(
                    "run_script", ["--file", "Temp/build-bundles.cs", "--entry", "BuildBundles.Status"]
                )
                print(f"  [{polls * 10 // 60:02d}:{polls * 10 % 60:02d}] {bundle_state}")
                if re.search(r'"result":"(success|failed)', bundle_state):
                    break
            if '"result":"success' not in bundle_state:
                detail = re.search(r'"result":"([^"]*)', bundle_state)
                raise RuntimeError(
                    f"bundles not built: {detail.group(1) if detail else bundle_state}; "
                    "if the editor is in Play mode, stop Play and retry (or use --skip-bundles)"
                )
            info("build/player", "bundles OK")

        if (profile.get("platform") or "") == "Android":
            manifest_original = sync_android_cleartext(project, profile)

        info("build/player", "== build dry_run ==")
        build_options = "null"
        if profile.get("channel") == "development":
            build_options = 'new string[]{ "Development", "DetailedBuildReport" }'
        output_expr = build_output_expr(profile)
        dry_json = builder.eval_file(
            "build-step.cs",
            f"return Unity.Pipeline.Editor.Commands.Build.BuildCommand.Build(options: {build_options}, "
            f"outputPath: {output_expr}, dryRun: true);",
        )
        dry = load_json(dry_json)
        if dry.get("result"):
            dry = dry["result"]
        if dry.get("status") == "error" or dry.get("valid") is False or dry.get("success") is False:
            raise RuntimeError(f"dry_run rejected: {dry_json}")
        info("build/player", "dry_run OK")

        info("build/player", "== build (confirm; IL2CPP may take 10-30 min) ==")
        build_start = datetime.now()
        response_json = builder.eval_file(
            "build-step.cs",
            f"return Unity.Pipeline.Editor.Commands.Build.BuildCommand.Build(options: {build_options}, "
            f"outputPath: {output_expr}, confirm: true);",
        )
        response = load_json(response_json)
        if response.get("result"):
            response = response["result"]
        if response.get("status") == "error" or response.get("success") is False:
            raise RuntimeError(f"build refused: {response_json}")

        timeout_min = 45
        polls = 0
        state = "unknown"
        done = False
        while not done and polls * 20 < timeout_min * 60:
            time.sleep(20)
            polls += 1
            try:
                state = load_json(builder.pipeline("build_status")).get("status") or "unknown"
                print(f"  [{polls * 20 // 60:02d}:{polls * 20 % 60:02d}] build_status: {state}")
            except Exception as exc:
                print(f"  [poll {polls}] status unavailable ({exc})")
            done = bool(re.search(r"completed|succeeded|finished|failed|error|cancelled|aborted", state))
        if not done:
            raise RuntimeError(f"build_status timeout after {timeout_min} min")
        final_json = builder.pipeline("build_status")
        if re.search(r'"(failed|error|cancelled|aborted)"\s*:\s*true', final_json) or re.search(
            r"status..:\s*.(failed|error|cancelled|aborted)", final_json
        ):
            raise RuntimeError(f"build FAILED: {final_json}")

        out_dir = project / str(profile.get("output"))
        glob_pattern = artifact_glob(profile)
        artifacts = sorted(path for path in out_dir.glob(glob_pattern) if path.is_file()) if out_dir.is_dir() else []
        if not artifacts:
            raise RuntimeError(f"no artifact ({glob_pattern}) under {out_dir} - artifact verification failed")
        artifact = artifacts[0]
        info("build/player", f"artifact: {artifact} ({artifact.stat().st_size / 1_048_576:.1f} MB)")
        cutoff = build_start.timestamp() - 60
        fresh = [path for path in out_dir.rglob("*") if path.is_file() and path.stat().st_mtime >= cutoff]
        if not fresh:
            raise RuntimeError(
                f"artifact stale: nothing under {out_dir} newer than {build_start:%H:%M:%S} - "
                "build produced nothing (check compile errors in Editor.log)"
            )
        info("build/player", f"freshness: {fresh[0].name}")
        info("build/player", "Player build PASSED")
        return EXIT_OK
    except RuntimeError as exc:
        return fail("build/player", str(exc))
    finally:
        restore_android_cleartext(project, manifest_original)


if __name__ == "__main__":
    sys.exit(main())
