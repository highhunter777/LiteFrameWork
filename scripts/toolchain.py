# -*- coding: utf-8 -*-
"""Single CLI facade for the development toolchain platform.

Commands: list / check / docs / open / run / generate / verify / build / doctor.
The manifest at scripts/gate/toolchain.json is the only tool registry.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from _toolchain_docs import check_docs, write_docs
from _toolchain_manifest import (
    ManifestError,
    default_manifest,
    find_tool,
    load_manifest,
    repo_root,
    sorted_tools,
    validate_manifest,
)
from _toolchain_scripts import check_scripts
from _toolchain_surfaces import check_surfaces

ROOT = repo_root()
DEFAULT_MANIFEST = default_manifest(ROOT)
DOC_INDEX = ROOT / "Docs" / "工具链" / "工具索引.md"
EDITOR_KINDS = {"page", "window", "action"}
EXIT_OK = 0
EXIT_FAIL = 1
EXIT_USAGE = 2
EXIT_NOT_FOUND = 4
EXIT_EDITOR_UNAVAILABLE = 5


class ToolchainError(Exception):
    def __init__(self, message, code=EXIT_FAIL):
        super().__init__(message)
        self.code = code


def _fail(message, code=EXIT_FAIL):
    print(f"[toolchain] FAILED: {message}", file=sys.stderr)
    return code


def _manifest_path(args):
    value = getattr(args, "manifest", None)
    return Path(value) if value else DEFAULT_MANIFEST


def _load(path):
    try:
        return load_manifest(path)
    except ManifestError as exc:
        raise ToolchainError(str(exc))


def _is_canonical(path):
    try:
        return Path(path).resolve() == DEFAULT_MANIFEST.resolve()
    except OSError:
        return False


def _print_issues(errors, warnings):
    for message in errors:
        print(f"[toolchain] ERROR: {message}", file=sys.stderr)
    for message in warnings:
        print(f"[toolchain] WARN: {message}")


def collect_checks(manifest_path, strict=False):
    manifest = _load(manifest_path)
    errors, warnings = validate_manifest(manifest, ROOT)
    for level, message in check_surfaces(manifest, ROOT):
        if level == "error":
            errors.append(message)
        else:
            warnings.append(message)
    if _is_canonical(manifest_path):
        ok, message = check_docs(manifest, ROOT, DOC_INDEX)
        if not ok:
            errors.append(message)
        for level, message in check_scripts(ROOT, manifest):
            if level == "error":
                errors.append(message)
            else:
                warnings.append(message)
    if strict:
        errors.extend(warnings)
        warnings = []
    return manifest, errors, warnings


def cmd_list(args):
    manifest = _load(_manifest_path(args))
    errors, _ = validate_manifest(manifest, ROOT)
    if errors:
        _print_issues(errors, [])
        return EXIT_FAIL
    if getattr(args, "json", False):
        payload = {
            "schemaVersion": manifest.get("schemaVersion"),
            "categories": manifest.get("categories", []),
            "tools": manifest.get("tools", []),
            "surfaces": manifest.get("surfaces", []),
        }
        print(json.dumps(payload, ensure_ascii=False, indent=2))
        return EXIT_OK
    print(f"[toolchain] manifest: {_manifest_path(args)}")
    for category in sorted(manifest.get("categories", []), key=lambda item: item.get("order", 1000)):
        print(f"\n{category.get('title', category.get('id'))}")
        for tool in sorted_tools(manifest, category.get("id")):
            print(f"  {tool['id']:<34} {tool.get('kind', ''):<8} {tool.get('title', '')}")
    return EXIT_OK


def cmd_check(args):
    _, errors, warnings = collect_checks(_manifest_path(args), strict=getattr(args, "strict", False))
    if getattr(args, "json", False):
        print(json.dumps({"ok": not errors, "errors": errors, "warnings": warnings}, ensure_ascii=False, indent=2))
    else:
        _print_issues(errors, warnings)
        if errors:
            print(f"[toolchain] check FAILED: {len(errors)} error(s), {len(warnings)} warning(s)", file=sys.stderr)
        else:
            print(f"[toolchain] check OK ({len(warnings)} warning(s))")
    return EXIT_FAIL if errors else EXIT_OK


def cmd_docs(args):
    manifest = _load(DEFAULT_MANIFEST)
    if getattr(args, "check", False):
        ok, message = check_docs(manifest, ROOT, DOC_INDEX)
        print(f"[toolchain] {message}")
        return EXIT_OK if ok else EXIT_FAIL
    changed = write_docs(manifest, ROOT, DOC_INDEX)
    print(f"[toolchain] docs {'updated' if changed else 'unchanged'}: {DOC_INDEX}")
    return EXIT_OK


def _unity_executable():
    executable = shutil.which("unity")
    if not executable:
        raise ToolchainError("unity CLI not found on PATH; start the editor and install the Unity CLI", EXIT_EDITOR_UNAVAILABLE)
    return executable


def cmd_open(args):
    manifest = _load(_manifest_path(args))
    tool = find_tool(manifest, args.id)
    if tool is None:
        raise ToolchainError(f"tool not found: {args.id}", EXIT_NOT_FOUND)
    if tool.get("kind") not in EDITOR_KINDS:
        raise ToolchainError(f"{args.id} is not an editor tool; use: python scripts/toolchain.py run {args.id}", EXIT_USAGE)
    port_file = ROOT / "Library" / "Pipeline" / ".unity-pipeline-port"
    if not port_file.is_file():
        raise ToolchainError("Unity editor pipeline is not running (Library/Pipeline/.unity-pipeline-port missing)", EXIT_EDITOR_UNAVAILABLE)
    unity = _unity_executable()
    script = ROOT / "Temp" / "toolchain-open.cs"
    script.parent.mkdir(parents=True, exist_ok=True)
    with open(script, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(
            "LiteGame.Toolchain.LiteGameToolboxWindow.Open(" + json.dumps(args.id) + ");\n"
        )
    result = subprocess.run(
        [
            unity,
            "command",
            "eval_file",
            "--file",
            "Temp/toolchain-open.cs",
            "--timeout",
            "30",
            "--project-path",
            str(ROOT),
            "--proxy-disable",
        ],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    output = (result.stdout or "") + (result.stderr or "")
    if result.returncode != 0:
        raise ToolchainError(f"failed to open {args.id}: {output.strip()}", result.returncode)
    print(f"[toolchain] opened {args.id}")
    return EXIT_OK


def _materialize_command(command):
    if not command:
        raise ToolchainError("tool has no command", EXIT_USAGE)
    resolved = list(command)
    if resolved[0] in {"python", "python3"}:
        resolved[0] = sys.executable
    return resolved


def cmd_run(args):
    manifest = _load(_manifest_path(args))
    tool = find_tool(manifest, args.id)
    if tool is None:
        raise ToolchainError(f"tool not found: {args.id}", EXIT_NOT_FOUND)
    if tool.get("kind") in EDITOR_KINDS:
        raise ToolchainError(f"{args.id} is an editor tool; use: python scripts/toolchain.py open {args.id}", EXIT_USAGE)
    command = _materialize_command(tool.get("command"))
    extra = list(getattr(args, "args", []) or [])
    if extra and extra[0] == "--":
        extra = extra[1:]
    return _run_child(command + extra)


def cmd_generate(args):
    toolbox = ROOT / "scripts" / "codegen" / "toolbox.py"
    if not toolbox.is_file():
        raise ToolchainError(f"generator toolbox missing: {toolbox}")
    return _run_child([sys.executable, str(toolbox)] + list(getattr(args, "args", []) or []))


def _pwsh():
    executable = shutil.which("pwsh")
    if not executable:
        raise ToolchainError("pwsh not found on PATH (project convention: PowerShell 7+)")
    return executable


def cmd_verify(args):
    lane = args.lane
    if lane == "L0":
        command = [sys.executable, str(ROOT / "scripts" / "gate" / "l0-dep-scan.py")]
    elif lane in {"L1", "L2", "L3", "All"}:
        command = [
            sys.executable,
            str(ROOT / "scripts" / "gate" / "test.py"),
            "--lane",
            lane,
            "--profile",
            args.profile,
        ]
    elif lane == "Doc":
        command = [
            sys.executable,
            str(ROOT / "scripts" / "gate" / "test.py"),
            "--lane",
            "Doc",
            "--profile",
            args.profile,
        ]
    else:
        raise ToolchainError(f"unsupported lane: {lane}", EXIT_USAGE)
    return _run_child(command)


def cmd_build(args):
    command = [sys.executable, str(ROOT / "scripts" / "build" / "build-player.py")]
    if args.profile:
        command += ["-Profile", args.profile]
    return _run_child(command)


def cmd_doctor(args):
    manifest_path = _manifest_path(args)
    _, errors, warnings = collect_checks(manifest_path, strict=getattr(args, "strict", False))
    _print_issues(errors, warnings)
    restore = _run_child([sys.executable, str(ROOT / "scripts" / "gate" / "restore-check.py")])
    port_file = ROOT / "Library" / "Pipeline" / ".unity-pipeline-port"
    print(f"[toolchain] pipeline: {'running' if port_file.is_file() else 'not running'} ({port_file})")
    fix_code = EXIT_OK
    if getattr(args, "fix_nightly", False):
        install = ROOT / "scripts" / "gate" / "install-nightly-task.py"
        if not install.is_file():
            raise ToolchainError(f"nightly installer missing: {install}")
        fix_code = _run_child([sys.executable, str(install), "--force"])
    if errors or restore != EXIT_OK or fix_code != EXIT_OK:
        print("[toolchain] doctor FAILED")
        return EXIT_FAIL
    print("[toolchain] doctor OK")
    return EXIT_OK


def _run_child(command):
    try:
        result = subprocess.run(command, cwd=str(ROOT))
        return result.returncode
    except FileNotFoundError as exc:
        raise ToolchainError(f"command not found: {command[0]} ({exc})")
    except OSError as exc:
        raise ToolchainError(f"command failed: {command[0]} ({exc})")


def build_parser():
    parser = argparse.ArgumentParser(
        prog="toolchain.py",
        description="Development toolchain platform: manifest-driven editor Hub + CLI.",
    )
    subparsers = parser.add_subparsers(dest="command")

    list_parser = subparsers.add_parser("list", help="list tools from the manifest")
    list_parser.add_argument("--json", action="store_true")
    list_parser.add_argument("--manifest")
    list_parser.set_defaults(func=cmd_list)

    check_parser = subparsers.add_parser("check", help="validate manifest, paths, docs and surfaces")
    check_parser.add_argument("--json", action="store_true")
    check_parser.add_argument("--strict", action="store_true", help="treat optional-surface warnings as errors")
    check_parser.add_argument("--manifest")
    check_parser.set_defaults(func=cmd_check)

    docs_parser = subparsers.add_parser("docs", help="generate or check the tool index")
    docs_parser.add_argument("--check", action="store_true")
    docs_parser.set_defaults(func=cmd_docs)

    open_parser = subparsers.add_parser("open", help="open an editor tool in the Hub")
    open_parser.add_argument("id")
    open_parser.add_argument("--manifest")
    open_parser.set_defaults(func=cmd_open)

    run_parser = subparsers.add_parser("run", help="run a CLI/gate/build/content tool")
    run_parser.add_argument("id")
    run_parser.add_argument("args", nargs=argparse.REMAINDER)
    run_parser.add_argument("--manifest")
    run_parser.set_defaults(func=cmd_run)

    generate_parser = subparsers.add_parser("generate", help="delegate to the generator toolbox")
    generate_parser.add_argument("args", nargs=argparse.REMAINDER)
    generate_parser.set_defaults(func=cmd_generate)

    verify_parser = subparsers.add_parser("verify", help="run a gate lane")
    verify_parser.add_argument("--lane", default="L1", choices=["L0", "L1", "L2", "L3", "Doc", "All"])
    verify_parser.add_argument("--profile", default="PullRequest", choices=["PullRequest", "Nightly", "Release"])
    verify_parser.set_defaults(func=cmd_verify)

    build_parser_cmd = subparsers.add_parser("build", help="delegate to build-player.py")
    build_parser_cmd.add_argument("--profile")
    build_parser_cmd.set_defaults(func=cmd_build)

    doctor_parser = subparsers.add_parser("doctor", help="check manifest, environment and surfaces")
    doctor_parser.add_argument("--strict", action="store_true")
    doctor_parser.add_argument("--fix-nightly", action="store_true")
    doctor_parser.add_argument("--manifest")
    doctor_parser.set_defaults(func=cmd_doctor)

    return parser


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    argv = list(sys.argv[1:] if argv is None else argv)
    if not argv:
        argv = ["list"]
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except ToolchainError as exc:
        return _fail(str(exc), exc.code)
    except ManifestError as exc:
        return _fail(str(exc))
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
