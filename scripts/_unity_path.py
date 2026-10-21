# -*- coding: utf-8 -*-
"""Resolve the Unity editor executable for L2 and environment checks.

Lookup order: --unity-exe -> UNITY_EXE -> LocalNotes/unity-editor.txt ->
Unity Hub metadata -> standard install roots -> registry. Machine-private
overrides stay outside the repository; auto-discovery never invents a path.
"""

import json
import os
import re
from pathlib import Path


class UnityPathError(RuntimeError):
    """Raised when no usable Unity editor can be resolved."""


LOCAL_CONFIG = Path("LocalNotes") / "unity-editor.txt"


def editor_version(project):
    path = Path(project) / "ProjectSettings" / "ProjectVersion.txt"
    if not path.is_file():
        return ""
    match = re.search(r"^m_EditorVersion:\s*(\S+)", path.read_text(encoding="utf-8", errors="replace"), re.M)
    return match.group(1) if match else ""


def _read_local_config(path):
    if not path.is_file():
        return ""
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        text = line.strip().strip('"').strip("'")
        if text and not text.startswith("#"):
            return text
    return ""


def _as_file(value, source):
    path = Path(value).expanduser()
    if path.is_file():
        return str(path.resolve()), source
    raise UnityPathError(f"{source} points to a missing Unity.exe: {path}")


def _candidate_paths(value):
    path = Path(value)
    if path.suffix.lower() == ".exe":
        return [path]
    return [path / "Editor" / "Unity.exe", path / "Unity.exe"]


def _hub_json_entries(path, expected):
    try:
        data = json.loads(path.read_text(encoding="utf-8", errors="replace"))
    except (OSError, ValueError):
        return []
    found = []

    def walk(node):
        if isinstance(node, dict):
            version = node.get("version")
            location = next(
                (node.get(key) for key in ("location", "path", "installPath", "editorPath") if node.get(key)),
                None,
            )
            if location and (not expected or not version or version == expected):
                found.append(Path(location))
            for value in node.values():
                walk(value)
        elif isinstance(node, list):
            for value in node:
                walk(value)

    walk(data)
    return found


def _secondary_roots(path, expected):
    try:
        data = json.loads(path.read_text(encoding="utf-8", errors="replace"))
    except (OSError, ValueError):
        return []
    values = data if isinstance(data, list) else [data]
    roots = []
    for value in values:
        if not isinstance(value, str) or not value.strip():
            continue
        root = Path(value.strip())
        roots.append(root / expected / "Editor" / "Unity.exe" if expected else root)
    return roots


def _registry_candidates():
    try:
        import winreg
    except ImportError:
        return []
    keys = (
        (winreg.HKEY_CURRENT_USER, r"Software\Unity Technologies\Installer\Unity"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Unity Technologies\Installer\Unity"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Unity Technologies\Installer\Unity"),
    )
    found = []
    for hive, key_path in keys:
        try:
            with winreg.OpenKey(hive, key_path) as key:
                for index in range(winreg.QueryInfoKey(key)[1]):
                    _, value, _ = winreg.EnumValue(key, index)
                    if isinstance(value, str) and value.strip():
                        found.extend(_candidate_paths(value.strip()))
        except OSError:
            continue
    return found


def _auto_candidates(project, expected, environ, include_registry):
    candidates = []
    appdata = environ.get("APPDATA")
    programdata = environ.get("PROGRAMDATA")
    for base in (appdata, programdata):
        if not base:
            continue
        hub_root = Path(base) / "UnityHub"
        for location in _hub_json_entries(hub_root / "editors.json", expected):
            for candidate in _candidate_paths(location):
                candidates.append((candidate, "Unity Hub editors.json"))
        for candidate in _secondary_roots(hub_root / "secondaryInstallPath.json", expected):
            candidates.append((candidate, "Unity Hub secondary install root"))

    install_bases = [
        environ.get("ProgramFiles"),
        environ.get("ProgramFiles(x86)"),
        str(Path(environ["LOCALAPPDATA"]) / "Programs") if environ.get("LOCALAPPDATA") else None,
    ]
    for base in install_bases:
        if not base:
            continue
        hub_editors = Path(base) / "Unity" / "Hub" / "Editor"
        if expected:
            candidates.append((hub_editors / expected / "Editor" / "Unity.exe", "Unity Hub standard install"))
        elif hub_editors.is_dir():
            for version_dir in sorted(hub_editors.iterdir()):
                candidates.append((version_dir / "Editor" / "Unity.exe", "Unity Hub standard install"))
        candidates.append((Path(base) / "Unity" / "Editor" / "Unity.exe", "legacy Unity install (version unverified)"))

    if include_registry:
        for candidate in _registry_candidates():
            candidates.append((candidate, "Unity registry"))

    seen = set()
    unique = []
    for candidate, source in candidates:
        key = str(candidate).lower()
        if key in seen:
            continue
        seen.add(key)
        unique.append((candidate, source))
    return unique


def resolve_unity_exe(project, override=None, environ=None, local_config=None, include_registry=True):
    """Return (unity.exe path, source label) or raise UnityPathError."""
    project = Path(project)
    environ = dict(os.environ if environ is None else environ)
    expected = editor_version(project)

    if override:
        return _as_file(override, "--unity-exe")
    if environ.get("UNITY_EXE"):
        return _as_file(environ["UNITY_EXE"], "UNITY_EXE")
    local_path = Path(local_config) if local_config else project / LOCAL_CONFIG
    local_value = _read_local_config(local_path)
    if local_value:
        return _as_file(local_value, str(LOCAL_CONFIG).replace("\\", "/"))

    for candidate, source in _auto_candidates(project, expected, environ, include_registry):
        if candidate.is_file():
            return str(candidate.resolve()), source

    expected_text = f" (expected Unity {expected})" if expected else ""
    raise UnityPathError(
        f"Unity editor not found{expected_text}. Set UNITY_EXE, pass --unity-exe, "
        f"write the path to {LOCAL_CONFIG.as_posix()}, or install it via Unity Hub."
    )
