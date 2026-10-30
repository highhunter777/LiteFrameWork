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

from _toolchain_defines import check_defines
from _toolchain_docs import check_docs, write_docs
from _toolchain_domains import check_domains
from _toolchain_manifest import (
    KINDS,
    PLANES,
    PURPOSES,
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
DOC_INDEX = ROOT / "Docs" / "说明" / "工具索引.md"
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
        for level, message in check_domains(manifest, ROOT):
            if level == "error":
                errors.append(message)
            else:
                warnings.append(message)
        for level, message in check_defines(manifest, ROOT):
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
    planes = set(getattr(args, "plane", None) or [])
    kinds = set(getattr(args, "kind", None) or [])
    purposes = set(getattr(args, "purpose", None) or [])
    print(f"[toolchain] manifest: {_manifest_path(args)}")
    if planes or kinds or purposes:
        print(
            f"[toolchain] filter: plane={sorted(planes) or 'any'} kind={sorted(kinds) or 'any'} "
            f"purpose={sorted(purposes) or 'any'}"
        )
    matched = 0
    for category in sorted(manifest.get("categories", []), key=lambda item: item.get("order", 1000)):
        tools = [
            tool
            for tool in sorted_tools(manifest, category.get("id"))
            if (not planes or tool.get("plane") in planes)
            and (not kinds or tool.get("kind") in kinds)
            and (not purposes or tool.get("purpose") in purposes)
        ]
        if not tools:
            continue
        matched += len(tools)
        print(f"\n{category.get('title', category.get('id'))}")
        for tool in tools:
            print(
                f"  {tool['id']:<30} {tool.get('kind', ''):<8} {tool.get('plane', ''):<9} "
                f"{tool.get('purpose', ''):<9} {tool.get('title', '')}"
            )
    print(f"[toolchain] {matched} tool(s)")
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
    if tool.get("kind") == "hud":
        raise ToolchainError(
            f"{args.id} is a runtime tool; enable it inside the game (F1), not from the Hub", EXIT_USAGE
        )
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


def _split_run_tokens(tokens):
    if "--" in tokens:
        index = tokens.index("--")
        return tokens[:index], tokens[index + 1:]
    return tokens, []


def _collect_option_values(tokens):
    values = {}
    index = 0
    while index < len(tokens):
        token = tokens[index]
        if token.startswith("--") and len(token) > 2:
            name = token[2:]
            value = None
            if "=" in name:
                name, value = name.split("=", 1)
            elif index + 1 < len(tokens) and not tokens[index + 1].startswith("--"):
                value = tokens[index + 1]
                index += 1
            values[name] = value
        index += 1
    return values


def _validate_tool_args(tool, tokens):
    specs = tool.get("args") or []
    if not specs:
        return
    values = _collect_option_values(tokens)
    missing = [f"--{spec['name']}" for spec in specs if spec.get("required") and spec["name"] not in values]
    if missing:
        raise ToolchainError(f"{tool['id']} missing required argument(s): {', '.join(missing)}", EXIT_USAGE)
    for spec in specs:
        name = spec["name"]
        if name not in values:
            continue
        value = values[name]
        arg_type = spec.get("type")
        label = f"--{name}"
        if arg_type == "bool":
            if value is not None and value.lower() not in {"true", "false", "1", "0"}:
                raise ToolchainError(f"{tool['id']} {label} expects a boolean", EXIT_USAGE)
        elif arg_type == "int":
            if value is None:
                raise ToolchainError(f"{tool['id']} {label} expects an integer", EXIT_USAGE)
            try:
                int(value)
            except ValueError:
                raise ToolchainError(f"{tool['id']} {label} expects an integer, got: {value}", EXIT_USAGE)
        elif arg_type == "enum":
            allowed = spec.get("values") or []
            if value not in allowed:
                raise ToolchainError(
                    f"{tool['id']} {label} must be one of: {', '.join(allowed)}", EXIT_USAGE
                )
        elif value is None or value == "":
            raise ToolchainError(f"{tool['id']} {label} requires a value", EXIT_USAGE)


def _print_run_help(tool, command):
    print(f"[toolchain] {tool['id']}: {tool.get('title', '')}")
    print(f"  command: {' '.join(command)}")
    specs = tool.get("args") or []
    if not specs:
        print("  args: none declared (pass tool options after --)")
    else:
        print("  args:")
        for spec in specs:
            details = ["required" if spec.get("required") else "optional"]
            if spec.get("values"):
                details.append("values: " + ", ".join(spec["values"]))
            if "default" in spec:
                details.append("default: " + json.dumps(spec["default"], ensure_ascii=False))
            description = spec.get("description", "")
            print(f"    --{spec['name']} <{spec.get('type')}> [{'; '.join(details)}] {description}".rstrip())
    print(f"  usage: python scripts/toolchain.py run {tool['id']} [--dry-run] -- [tool args]")
    return EXIT_OK


def _print_run_usage():
    print("usage: python scripts/toolchain.py run [--manifest <path>] <id> [--help] [--dry-run] -- [tool args]")
    return EXIT_OK


def _run_tool(manifest_value, tool_id, tokens):
    manifest = _load(Path(manifest_value) if manifest_value else DEFAULT_MANIFEST)
    tool = find_tool(manifest, tool_id)
    if tool is None:
        raise ToolchainError(f"tool not found: {tool_id}", EXIT_NOT_FOUND)
    if tool.get("kind") == "hud":
        raise ToolchainError(f"{tool_id} is a runtime tool; enable it inside the game (F1)", EXIT_USAGE)
    if tool.get("kind") in EDITOR_KINDS:
        raise ToolchainError(f"{tool_id} is an editor tool; use: python scripts/toolchain.py open {tool_id}", EXIT_USAGE)
    command = _materialize_command(tool.get("command"))
    head, tail = _split_run_tokens(list(tokens))
    if "--help" in head:
        return _print_run_help(tool, command)
    unknown = [token for token in head if token != "--dry-run"]
    if unknown:
        raise ToolchainError(
            f"{tool_id}: tool arguments must follow -- (got: {' '.join(unknown)})", EXIT_USAGE
        )
    dry_run = "--dry-run" in head
    if dry_run:
        safety = tool.get("safety") or {}
        if safety.get("dryRun") != "supported":
            raise ToolchainError(
                f"{tool_id} does not support run --dry-run; pass the tool's own flag after -- "
                f"(e.g. run {tool_id} -- --dry-run)",
                EXIT_USAGE,
            )
        command.append(safety.get("dryRunArg", "--dry-run"))
    _validate_tool_args(tool, tail)
    code = _run_child(command + tail)
    print(f"[toolchain] {tool_id} exit={code}")
    return code


def cmd_run(argv):
    """Parse run arguments before argparse so the literal `--` separator survives."""
    manifest_value = None
    tool_id = None
    tokens = []
    index = 0
    while index < len(argv):
        token = argv[index]
        if tool_id is not None:
            tokens = argv[index:]
            break
        if token in {"-h", "--help"}:
            return _print_run_usage()
        if token == "--manifest":
            if index + 1 >= len(argv):
                raise ToolchainError("--manifest requires a path", EXIT_USAGE)
            manifest_value = argv[index + 1]
            index += 2
            continue
        if token.startswith("--manifest="):
            manifest_value = token.split("=", 1)[1]
            index += 1
            continue
        if token.startswith("-"):
            raise ToolchainError(f"run: unknown option {token}", EXIT_USAGE)
        tool_id = token
        index += 1
    if tool_id is None:
        _print_run_usage()
        raise ToolchainError("run requires a tool id", EXIT_USAGE)
    return _run_tool(manifest_value, tool_id, tokens)


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
    suite = args.suite
    if suite == "L0":
        command = [sys.executable, str(ROOT / "scripts" / "gate" / "l0-dep-scan.py")]
    elif suite in {"L1", "L2", "L3", "All"}:
        command = [
            sys.executable,
            str(ROOT / "scripts" / "gate" / "test.py"),
            "--suite",
            suite,
            "--profile",
            args.profile,
        ]
    elif suite == "Doc":
        command = [
            sys.executable,
            str(ROOT / "scripts" / "gate" / "test.py"),
            "--suite",
            "Doc",
            "--profile",
            args.profile,
        ]
    else:
        raise ToolchainError(f"unsupported suite: {suite}", EXIT_USAGE)
    return _run_child(command)


def build_command(profile=None):
    command = [sys.executable, str(ROOT / "scripts" / "build" / "build-player.py")]
    if profile:
        command += ["--profile", profile]
    return command


def cmd_build(args):
    return _run_child(build_command(args.profile))


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
    list_parser.add_argument("--plane", action="append", choices=sorted(PLANES), help="filter by plane (repeatable)")
    list_parser.add_argument("--kind", action="append", choices=sorted(KINDS), help="filter by kind (repeatable)")
    list_parser.add_argument(
        "--purpose", action="append", choices=sorted(PURPOSES), help="filter by capability line (repeatable)"
    )
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

    # Dispatched in main() before argparse so the literal `--` separator survives.
    subparsers.add_parser("run", help="run a CLI/gate/build/content tool")

    generate_parser = subparsers.add_parser("generate", help="delegate to the generator toolbox")
    generate_parser.add_argument("args", nargs=argparse.REMAINDER)
    generate_parser.set_defaults(func=cmd_generate)

    verify_parser = subparsers.add_parser("verify", help="run a gate suite")
    verify_parser.add_argument("--suite", default="L1", choices=["L0", "L1", "L2", "L3", "Doc", "All"])
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
    try:
        if argv[0] == "run":
            return cmd_run(argv[1:])
        parser = build_parser()
        args = parser.parse_args(argv)
        return args.func(args)
    except ToolchainError as exc:
        return _fail(str(exc), exc.code)
    except ManifestError as exc:
        return _fail(str(exc))
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
