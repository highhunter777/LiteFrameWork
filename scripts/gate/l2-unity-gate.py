# -*- coding: utf-8 -*-
"""L2 Unity gate: meta scan + Pipeline/batchmode EditMode/PlayMode checks."""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402

DEFAULT_UNITY_EXE = os.environ.get("UNITY_EXE", r"E:\unity3d\2022.3.55f1c1\Editor\Unity.exe")
GUID_RE = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$")


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="L2 Unity gate")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--unity-exe", default=DEFAULT_UNITY_EXE)
    parser.add_argument("--test-output", default=r"TestResults\editmode-results.xml")
    parser.add_argument("--meta-scan-only", action="store_true")
    parser.add_argument("--run-editmode-tests", action="store_true")
    return parser.parse_args(argv)


class L2:
    def __init__(self, project, unity_exe):
        self.project = project
        self.unity_exe = unity_exe
        self.failures = []
        self.notes = []

    def ok(self, message):
        info("gate/l2", f"PASS: {message}")

    def bad(self, message):
        self.failures.append(message)
        info("gate/l2", f"FAIL: {message}")

    def note(self, message):
        self.notes.append(message)
        info("gate/l2", f"INFO: {message}")

    def scan_meta(self):
        roots = [self.project / "Assets", self.project / "Packages"]
        count = 0
        bad = []
        for root in roots:
            if not root.is_dir():
                continue
            for path in root.rglob("*.meta"):
                count += 1
                try:
                    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()[:8]
                except OSError:
                    lines = []
                guid = next((line.strip() for line in lines if line.startswith("guid:")), None)
                if guid is None:
                    bad.append(f"{path}  (missing guid line)")
                elif not GUID_RE.match(guid):
                    bad.append(f"{path}  ({guid})")
        if bad:
            self.bad(f"发现 {len(bad)} 个非法 GUID 的 .meta（Unity 会拒绝导入）")
            for item in bad[:20]:
                print(f"        {item}")
            print("        fix: rewrite to uuid4().hex (32 hex); old invalid GUIDs were never referenced")
        else:
            self.ok(f"scanned {count} .meta files: all GUIDs are legal 32-hex")

    def have_unity_cli(self):
        return shutil.which("unity") is not None

    def pipeline(self, command, args=None, timeout=10):
        unity = shutil.which("unity")
        if not unity:
            raise RuntimeError("unity CLI not found on PATH")
        cmd = [unity, "command", "--timeout", str(timeout), command]
        cmd.extend(args or [])
        cmd.extend(["--project-path", str(self.project), "--proxy-disable"])
        result = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        output = (result.stdout or "") + (result.stderr or "")
        if result.returncode != 0:
            raise RuntimeError(f"unity command {command} failed (exit {result.returncode}): {output.strip()}")
        return output

    def tests_async(self, mode, timeout_seconds=900):
        self.pipeline("run_tests", ["--mode", mode, "--async_tests", "true"], timeout=60)
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            time.sleep(5)
            text = self.pipeline("test_status", timeout=30)
            if re.search(r'"status"\s*:\s*"running"', text):
                continue
            if re.search(r'"status"\s*:\s*"(completed|failed|idle)', text):
                return text
        raise RuntimeError(f"{mode} tests timed out after {timeout_seconds}s")

    @staticmethod
    def json_int(text, key):
        match = re.search(rf'(?i)"{re.escape(key)}"\s*:\s*(\d+)', text)
        return int(match.group(1)) if match else -1

    def newest_source_time(self):
        assets = self.project / "Assets"
        files = []
        if assets.is_dir():
            for path in assets.rglob("*.cs"):
                if any(part.startswith(".") for part in path.parts):
                    continue
                files.append(path)
        return max((path.stat().st_mtime for path in files), default=0)

    def newest_assembly_time(self):
        directory = self.project / "Library" / "ScriptAssemblies"
        if not directory.is_dir():
            return 0
        return max((path.stat().st_mtime for path in directory.glob("*.dll")), default=0)

    def unity_process_running(self):
        try:
            result = subprocess.run(
                ["tasklist", "/FI", "IMAGENAME eq Unity.exe", "/NH"],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
        except OSError:
            return False
        return "Unity.exe" in (result.stdout or "")

    def run_pipeline_mode(self):
        descriptor = self.project / "Library" / "Pipeline" / ".unity-pipeline-port"
        if not self.have_unity_cli():
            self.note("unity CLI not found on PATH; skipping Unity-side checks")
            return
        try:
            desc = json.loads(descriptor.read_text(encoding="utf-8"))
            self.note(f"editor descriptor: port {desc.get('port')} / pid {desc.get('pid')} / {desc.get('unityVersion')}")
        except Exception as exc:
            self.bad(f"editor descriptor unreadable: {exc}")
            return
        try:
            self.pipeline("set_autotick", ["--enable", "true"])
            before = self.newest_source_time()
            refresh_done = True
            try:
                self.pipeline("clear_console")
                temp = self.project / "Temp"
                temp.mkdir(parents=True, exist_ok=True)
                (temp / "l2-freshness-request.cs").write_text(
                    "UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);\n"
                    "UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();\n",
                    encoding="utf-8",
                )
                (temp / "l2-freshness-status.cs").write_text(
                    "return UnityEditor.EditorApplication.isCompiling ? 1 : 0;", encoding="utf-8"
                )
                self.pipeline("eval_file", ["--file", "Temp/l2-freshness-request.cs", "--timeout", "30000"])
                waited = 0
                while waited < 90:
                    time.sleep(3)
                    waited += 3
                    busy = self.pipeline("eval_file", ["--file", "Temp/l2-freshness-status.cs", "--timeout", "30000"])
                    if not re.search(r'"result"\s*:\s*"?1', busy):
                        break
            except Exception as exc:
                refresh_done = False
                self.note(f"freshness refresh did not complete ({exc}) - checking current state")
            rec = self.pipeline("recompile_status")
            print(f"  recompile_status: {rec.strip()}")
            after = self.newest_assembly_time()
            if before > after:
                if not refresh_done:
                    self.bad("source newer than assemblies and refresh did not complete - EditMode may run stale code")
                else:
                    self.ok("assemblies fresh (Unity verdict authoritative; mtime newer means content-identical rewrite)")
            else:
                self.ok("assemblies fresh (source <= assemblies)")
            if re.search(r'"compilationFailed"\s*:\s*true|"failed"\s*:\s*true', rec):
                self.bad("Unity compilation failed (recompile_status failed/compilationFailed=true)")
            else:
                self.ok("Unity compilation status normal")
            console = self.pipeline("console")
            if re.search(r"error CS\d+", console):
                match = re.search(r"error CS\d+[^\"\\]*", console)
                self.bad(f"console compilation error: {match.group(0).strip() if match else 'error CS'}")
            else:
                errors = len(re.findall(r'"level":"error"', console))
                self.ok(f"console has no CS compilation errors (error-level entries: {errors})")
                if errors:
                    self.note(f"{errors} non-compilation error entries; inspect with: unity command console --project-path \"{self.project}\"")
            for mode, label in (("editor", "EditMode"), ("playmode", "PlayMode")):
                result = self.tests_async(mode)
                total = self.json_int(result, "Total")
                passed = self.json_int(result, "Passed")
                failed = self.json_int(result, "Failed")
                print(f"  run_tests({label}): Total={total} Passed={passed} Failed={failed}")
                if total <= 0:
                    self.bad(f"{label} test count = 0 (test assembly not compiled in?)")
                elif failed > 0:
                    self.bad(f"{label} failed {failed} of {total}")
                else:
                    self.ok(f"{label} all green ({passed}/{total})")
        except Exception as exc:
            self.bad(f"Pipeline command failed: {exc}")

    def run_batchmode(self, test_output):
        if self.unity_process_running():
            self.bad("Unity process is running; batchmode cannot share the project - close the editor or use Pipeline mode")
            return
        if not Path(self.unity_exe).is_file():
            self.bad(f"Unity editor not found: {self.unity_exe} (use --unity-exe)")
            return
        if not self.have_unity_cli():
            self.bad("unity CLI not found on PATH")
            return
        output_dir = self.project / Path(test_output).parent
        output_dir.mkdir(parents=True, exist_ok=True)
        for mode in ("EditMode", "PlayMode"):
            out_file = test_output if mode == "EditMode" else r"TestResults\playmode-results.xml"
            self.note(f"running unity test --mode {mode} --output {out_file}")
            result = subprocess.run(
                [
                    shutil.which("unity"),
                    "test",
                    str(self.project),
                    "-e",
                    self.unity_exe,
                    "--mode",
                    mode,
                    "--output",
                    out_file,
                    "--timeout",
                    "1800",
                ],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
            )
            output = (result.stdout or "") + (result.stderr or "")
            for line in output.splitlines():
                print(f"    {line}")
            if result.returncode != 0:
                self.bad(f"{mode} tests failed (exit {result.returncode}), report: {out_file}")
            else:
                self.ok(f"{mode} tests passed, report: {out_file}")


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
    gate = L2(project, args.unity_exe)
    info("gate/l2", f"L2 Unity gate | project: {project}")

    info("gate/l2", "=== 1. illegal meta GUID scan ===")
    gate.scan_meta()
    if args.meta_scan_only:
        if gate.failures:
            return fail("gate/l2", f"L2(meta) failed: {len(gate.failures)} item(s)")
        info("gate/l2", "L2(meta) passed")
        return EXIT_OK

    descriptor = project / "Library" / "Pipeline" / ".unity-pipeline-port"
    if descriptor.is_file() and not args.run_editmode_tests:
        info("gate/l2", "=== 2. Unity-side status (editor running, Pipeline) ===")
        gate.run_pipeline_mode()
    else:
        info("gate/l2", "=== 2. Unity-side tests (batchmode: EditMode + PlayMode) ===")
        gate.run_batchmode(args.test_output)

    info("gate/l2", "=== summary ===")
    for note in list(gate.notes):
        print(f"  [INFO] {note}")
    if not gate.failures:
        info("gate/l2", "L2 passed")
        return EXIT_OK
    info("gate/l2", f"L2 failed: {len(gate.failures)} item(s)")
    for failure in gate.failures:
        print(f"    - {failure}")
    return EXIT_FAIL


if __name__ == "__main__":
    sys.exit(main())
