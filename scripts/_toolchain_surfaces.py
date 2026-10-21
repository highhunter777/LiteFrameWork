# -*- coding: utf-8 -*-
"""External registration surface checks (CI / hooks / Windows scheduled task / paths)."""

import os
import re
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

SCRIPT_RE = re.compile(
    r"(?<![\w.-])(?:\./)?((?:scripts|Server|Assets|Tests)/[A-Za-z0-9_./-]+\.(?:ps1|py))"
)


def _level(surface):
    return "error" if surface.get("required", True) else "warning"


def _missing_scripts(root, text):
    missing = []
    for match in SCRIPT_RE.finditer(text):
        relative = match.group(1)
        if not (Path(root) / relative).is_file():
            missing.append(relative)
    return sorted(set(missing))


def check_ci_surface(surface, root):
    path = Path(root) / surface.get("path", "")
    if not path.is_file():
        return [(_level(surface), f"CI surface missing: {surface.get('path')}")]
    missing = _missing_scripts(root, path.read_text(encoding="utf-8", errors="replace"))
    return [
        (_level(surface), f"CI references missing script: {relative}")
        for relative in missing
    ]


def check_hook_surface(surface, root):
    path = Path(root) / surface.get("path", "")
    if not path.is_file():
        return [(_level(surface), f"hook config missing: {surface.get('path')}")]
    missing = _missing_scripts(root, path.read_text(encoding="utf-8", errors="replace"))
    return [
        (_level(surface), f"{surface.get('path')} references missing script: {relative}")
        for relative in missing
    ]


def check_path_surface(surface, root):
    path = Path(root) / surface.get("path", "")
    if path.exists():
        return []
    return [(_level(surface), f"optional path missing: {surface.get('path')}")]


def _decode_process_bytes(raw):
    if raw.startswith(b"\xff\xfe") or raw.startswith(b"\xfe\xff"):
        return raw.decode("utf-16", errors="replace")
    for encoding in ("utf-8", "gbk"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            continue
    return raw.decode("utf-8", errors="replace")


def _query_scheduled_task(name):
    try:
        result = subprocess.run(
            ["schtasks", "/query", "/tn", name, "/xml"],
            capture_output=True,
            timeout=20,
        )
    except FileNotFoundError:
        return None, "schtasks not found"
    except subprocess.TimeoutExpired:
        return None, "schtasks timed out"
    if result.returncode != 0:
        return None, f"scheduled task not found: {name}"
    return _decode_process_bytes(result.stdout), None


def check_scheduled_task_surface(surface):
    if os.name != "nt":
        return [(_level(surface), f"scheduled task check skipped on this OS: {surface.get('name')}")]
    name = surface.get("name", "")
    xml_text, error = _query_scheduled_task(name)
    if error:
        return [(_level(surface), f"{error}")]
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError as exc:
        return [(_level(surface), f"scheduled task {name} XML parse failed: {exc}")]

    values = {}
    for element in root.iter():
        local = element.tag.rsplit("}", 1)[-1]
        if local in {"Command", "Arguments"} and local not in values:
            values[local] = element.text or ""
    command = values.get("Command", "")
    arguments = values.get("Arguments", "")
    expected_script = surface.get("script", "").replace("/", "\\").lower()
    expected_runner = (surface.get("runner") or "pwsh").lower()
    issues = []
    if expected_runner not in command.lower():
        issues.append(
            (_level(surface), f"scheduled task {name} runner is '{command or '?'}', expected '{expected_runner}'")
        )
    arguments_norm = arguments.replace("/", "\\").lower()
    if expected_script and expected_script not in arguments_norm:
        issues.append(
            (_level(surface), f"scheduled task {name} does not point at {surface.get('script')}")
        )
    return issues


def check_surfaces(manifest, root):
    issues = []
    for surface in manifest.get("surfaces", []):
        kind = surface.get("kind")
        if kind == "ci-workflow":
            issues.extend(check_ci_surface(surface, root))
        elif kind == "hook-config":
            issues.extend(check_hook_surface(surface, root))
        elif kind == "scheduled-task":
            issues.extend(check_scheduled_task_surface(surface))
        elif kind == "path":
            issues.extend(check_path_surface(surface, root))
        else:
            issues.append(("error", f"unknown surface kind: {kind}"))
    return issues
