#!/usr/bin/env python3
"""HybridCLR install / status / generate entry.

Batchmode by default (editor must be closed); --pipeline drives a running editor.
"""
import argparse
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402
from _unity_path import UnityPathError, resolve_unity_exe  # noqa: E402

TOOL = "build/hybridclr"
METHOD_TYPE = "LiteGame.Editor.Toolchain.HybridCLRTool"
COMMANDS = ("status", "install", "generate")
BATCH_METHODS = {"status": "Status", "install": "Install", "generate": "GenerateAll"}
PIPELINE_CODE = {
    "status": (
        "var i = new HybridCLR.Installer.InstallerController();\n"
        "UnityEngine.Debug.Log($\"[hclr] pkg={i.PackageVersion} hclr={i.HybridclrLocalVersion} "
        "il2cppPlus={i.Il2cppPlusLocalVersion} installed={i.HasInstalledHybridCLR()}\");\n"
        "return \"installed=\" + i.HasInstalledHybridCLR();"
    ),
    "install": "LiteGame.Editor.Toolchain.HybridCLRTool.Install();\nreturn \"install ok\";",
    "generate": "LiteGame.Editor.Toolchain.HybridCLRTool.GenerateAll();\nreturn \"generate ok\";",
}


def installed(project: Path) -> bool:
    root = project / "Library" / "HybridCLR"
    if not root.is_dir():
        return False
    return any(root.glob("LocalIl2CppData-*/il2cpp/libil2cpp/hybridclr"))


def tail(path: Path, limit: int = 20) -> str:
    if not path.is_file():
        return ""
    return "\n".join(path.read_text(encoding="utf-8", errors="replace").splitlines()[-limit:])


def run_batchmode(project: Path, command: str, timeout: int) -> int:
    if (project / "Temp" / "UnityLockfile").is_file():
        return fail(TOOL, "editor is open on this project; close it or use --pipeline")
    try:
        editor, source = resolve_unity_exe(project)
    except UnityPathError as exc:
        return fail(TOOL, f"Unity editor not resolved: {exc}")
    info(TOOL, f"editor: {editor} ({source})")
    log = project / "Temp" / f"hybridclr-{command}.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        [str(editor), "-batchmode", "-quit", "-projectPath", str(project),
         "-executeMethod", f"{METHOD_TYPE}.{BATCH_METHODS[command]}", "-logFile", str(log)],
        capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=timeout,
    )
    if result.returncode != 0:
        print(tail(log))
        return fail(TOOL, f"{command} failed (exit {result.returncode}); log: {log}")
    info(TOOL, f"{command} ok")
    return EXIT_OK


def run_pipeline(project: Path, command: str, timeout: int) -> int:
    unity = shutil.which("unity")
    if not unity:
        return fail(TOOL, "unity CLI not found on PATH (needed for --pipeline)")
    snippet = project / "Temp" / f"hybridclr-{command}.cs"
    snippet.parent.mkdir(parents=True, exist_ok=True)
    snippet.write_text(PIPELINE_CODE[command], encoding="utf-8")
    result = subprocess.run(
        [unity, "command", "eval_file", "--file", f"Temp/{snippet.name}", "--timeout", str(timeout),
         "--project-path", str(project), "--proxy-disable"],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
    )
    output = (result.stdout or "") + (result.stderr or "")
    print(output.strip())
    if result.returncode != 0:
        return fail(TOOL, f"{command} failed via pipeline (exit {result.returncode})")
    return EXIT_OK


def main(argv=None) -> int:
    configure_stdio()
    parser = argparse.ArgumentParser(description="HybridCLR install/status/generate")
    parser.add_argument("command", choices=COMMANDS)
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--pipeline", action="store_true", help="drive a running editor instead of batchmode")
    parser.add_argument("--timeout", type=int, default=1800)
    args = parser.parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()

    package = project / "Packages" / "com.code-philosophy.hybridclr" / "package.json"
    if not package.is_file():
        return fail(TOOL, "package missing: Packages/com.code-philosophy.hybridclr")

    if args.command == "status" and not args.pipeline:
        info(TOOL, f"installed={installed(project)} (Library/HybridCLR)")
        return EXIT_OK
    if args.pipeline:
        return run_pipeline(project, args.command, args.timeout)
    return run_batchmode(project, args.command, args.timeout)


if __name__ == "__main__":
    sys.exit(main())
