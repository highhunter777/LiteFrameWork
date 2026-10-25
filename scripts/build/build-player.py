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
            builder.eval_file(
                "build-backend-switch.cs",
                "UnityEditor.Build.NamedBuildTarget t = UnityEditor.Build.NamedBuildTarget.Standalone;\n"
                "UnityEditor.PlayerSettings.SetScriptingBackend(t, UnityEditor.ScriptingImplementation.IL2CPP);\n"
                "return UnityEditor.PlayerSettings.GetScriptingBackend(t).ToString();\n",
            )
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
        elif profile.get("platform") != "StandaloneWindows64":
            raise RuntimeError(
                f"profile platform is {profile.get('platform')} - only StandaloneWindows64 content is wired; "
                "use --skip-bundles to acknowledge"
            )
        else:
            info("build/player", "== bundles: build builtin content bundles (Offline) ==")
            bundle_cs = """public static class BuildBundles
{
    public static string Start()
    {
        LiteGame.EditorTools.BuiltinBundleBuildInvoker.RunAsync();
        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status;
    }

    public static string Status()
    {
        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status
             + " | out=" + LiteGame.EditorTools.BuiltinBundleBuildInvoker.LastOutputDirectory;
    }
}
"""
            builder.eval_file("build-bundles.cs", bundle_cs)
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

        info("build/player", "== build dry_run ==")
        build_options = "null"
        if profile.get("channel") == "development":
            build_options = 'new string[]{ "Development", "DetailedBuildReport" }'
        output_expr = (
            'System.IO.Path.Combine("' + str(profile.get("output")) + '", '
            "UnityEditor.PlayerSettings.productName + \".exe\")"
        )
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
        artifact_glob = profile.get("artifactGlob") or "*.exe"
        artifacts = sorted(path for path in out_dir.glob(artifact_glob) if path.is_file()) if out_dir.is_dir() else []
        if not artifacts:
            raise RuntimeError(f"no artifact ({artifact_glob}) under {out_dir} - artifact verification failed")
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


if __name__ == "__main__":
    sys.exit(main())
