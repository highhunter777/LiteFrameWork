# -*- coding: utf-8 -*-
"""Static script-form checks for the toolchain platform.

Checks are intentionally static: they never execute project scripts, Unity or
cloud commands. Two surfaces are covered:

* orphan executable scripts (not registered, not internal/test/one-shot);
* encoding and line ending form for scripts under scripts/.
"""

import re
from pathlib import Path

SCRIPT_SUFFIXES = {".py", ".ps1"}
EXCLUDED_PARTS = {"__pycache__", ".venv", "node_modules"}
GLOBAL_ENTRY = "scripts/toolchain.py"
REFERENCE_RE = re.compile(r"(?<![\w.-])((?:scripts)[/\\][A-Za-z0-9_./\\-]+\.(?:py|ps1))")
PY_MAIN_RE = re.compile(r"(?m)^\s*def\s+main\s*\(")
PY_GUARD_RE = re.compile(r"(?m)^\s*if\s+__name__\s*==")
PS_PARAM_RE = re.compile(r"(?m)^\s*param\s*\(")


def _read_text(path):
    return path.read_text(encoding="utf-8-sig", errors="replace")


def _normalize(relative):
    relative = relative.replace("\\", "/")
    return relative[2:] if relative.startswith("./") else relative


def _script_files(root):
    for path in sorted((Path(root) / "scripts").rglob("*")):
        if not path.is_file() or path.suffix.lower() not in SCRIPT_SUFFIXES:
            continue
        if any(part in EXCLUDED_PARTS for part in path.parts):
            continue
        yield path


def _registered_paths(manifest):
    paths = set()
    for tool in manifest.get("tools", []):
        for token in tool.get("command") or []:
            if not isinstance(token, str):
                continue
            token = _normalize(token)
            if token.startswith("scripts/") and token.endswith((".py", ".ps1")):
                paths.add(token)
    return paths


def _surface_paths(root, manifest):
    paths = set()
    for surface in manifest.get("surfaces", []):
        kind = surface.get("kind")
        if kind in {"ci-workflow", "hook-config"}:
            path = Path(root) / str(surface.get("path", ""))
            if path.is_file():
                for match in REFERENCE_RE.finditer(_read_text(path)):
                    paths.add(_normalize(match.group(1)))
        elif kind == "scheduled-task" and surface.get("script"):
            paths.add(_normalize(str(surface["script"])))
    return paths


def _is_executable(path, text):
    if path.suffix.lower() == ".py":
        return bool(PY_MAIN_RE.search(text) or PY_GUARD_RE.search(text))
    return bool(PS_PARAM_RE.search(text))


def _is_exempt(relative, path, text, registered, registered_texts, surface_paths):
    if relative == GLOBAL_ENTRY:
        return True
    if path.name.startswith("_"):
        return True
    if relative.startswith("scripts/tests/"):
        return True
    if relative in registered or relative in surface_paths:
        return True
    if "@oneshot" in "\n".join(text.splitlines()[:20]):
        return True
    name_pattern = re.compile(r"(?<![\w.-])" + re.escape(path.name) + r"(?![\w.-])")
    return any(name_pattern.search(parent_text) for parent_text in registered_texts)


def check_scripts(root, manifest, strict=False):
    """Return [(level, message)] for orphan and encoding/EOL issues."""
    root = Path(root)
    level = "error" if strict else "warning"
    issues = []
    registered = _registered_paths(manifest)
    registered_texts = []
    for relative in sorted(registered):
        path = root / relative
        if path.is_file():
            registered_texts.append(_read_text(path))
    surface_paths = _surface_paths(root, manifest)

    for path in _script_files(root):
        relative = path.relative_to(root).as_posix()
        text = _read_text(path)
        if _is_executable(path, text) and not _is_exempt(
            relative, path, text, registered, registered_texts, surface_paths
        ):
            issues.append((level, f"unregistered executable script: {relative}"))

        raw = path.read_bytes()
        has_bom = raw.startswith(b"\xef\xbb\xbf")
        has_crlf = b"\r\n" in raw
        if has_crlf:
            issues.append((level, f"script must use LF instead of CRLF: {relative}"))
        if path.suffix.lower() == ".py" and has_bom:
            issues.append((level, f"Python script must be UTF-8 without BOM: {relative}"))
        if path.suffix.lower() == ".ps1" and not has_bom and any(byte > 127 for byte in raw):
            issues.append((level, f"PowerShell script has non-ASCII text without UTF-8 BOM: {relative}"))

    return issues
