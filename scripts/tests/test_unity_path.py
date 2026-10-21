# -*- coding: utf-8 -*-
"""Contracts for the Unity editor path resolution chain."""

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from _unity_path import UnityPathError, editor_version, resolve_unity_exe  # noqa: E402

VERSION = "2022.3.55f1c1"


def make_project(temp, version=VERSION):
    project = Path(temp) / "project"
    settings = project / "ProjectSettings"
    settings.mkdir(parents=True)
    (settings / "ProjectVersion.txt").write_text(f"m_EditorVersion: {version}\n", encoding="utf-8")
    return project


def make_exe(path):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("stub", encoding="utf-8")
    return path


def empty_env(temp):
    base = Path(temp) / "env"
    base.mkdir()
    return {
        "APPDATA": str(base / "appdata"),
        "PROGRAMDATA": str(base / "programdata"),
        "ProgramFiles": str(base / "programfiles"),
        "LOCALAPPDATA": str(base / "localappdata"),
    }


class UnityPathTests(unittest.TestCase):
    def test_editor_version_reads_project_settings(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            self.assertEqual(editor_version(project), VERSION)

    def test_override_wins_over_env_and_local_config(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            override = make_exe(Path(temp) / "override" / "Unity.exe")
            env_exe = make_exe(Path(temp) / "env" / "Unity.exe")
            local = Path(temp) / "local.txt"
            local.write_text(str(make_exe(Path(temp) / "local" / "Unity.exe")), encoding="utf-8")
            path, source = resolve_unity_exe(project, str(override), {"UNITY_EXE": str(env_exe)}, local, False)
        self.assertEqual(Path(path), override.resolve())
        self.assertEqual(source, "--unity-exe")

    def test_env_wins_over_local_config(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            env_exe = make_exe(Path(temp) / "env" / "Unity.exe")
            local = Path(temp) / "local.txt"
            local.write_text(str(make_exe(Path(temp) / "local" / "Unity.exe")), encoding="utf-8")
            path, source = resolve_unity_exe(project, None, {"UNITY_EXE": str(env_exe)}, local, False)
        self.assertEqual(Path(path), env_exe.resolve())
        self.assertEqual(source, "UNITY_EXE")

    def test_local_notes_file_is_used(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            local_exe = make_exe(Path(temp) / "local" / "Unity.exe")
            local = project / "LocalNotes" / "unity-editor.txt"
            local.parent.mkdir()
            local.write_text(f"# machine path\n{local_exe}\n", encoding="utf-8")
            path, source = resolve_unity_exe(project, None, {}, None, False)
        self.assertEqual(Path(path), local_exe.resolve())
        self.assertIn("LocalNotes", source)

    def test_hub_editors_json_is_used(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            env = empty_env(temp)
            hub_dir = Path(env["APPDATA"]) / "UnityHub"
            hub_dir.mkdir(parents=True)
            install = Path(temp) / "hub" / VERSION
            make_exe(install / "Editor" / "Unity.exe")
            (hub_dir / "editors.json").write_text(
                json.dumps({"editors": [{"version": VERSION, "location": str(install)}]}),
                encoding="utf-8",
            )
            path, source = resolve_unity_exe(project, None, env, None, False)
        self.assertEqual(Path(path), (install / "Editor" / "Unity.exe").resolve())
        self.assertIn("Hub", source)

    def test_wrong_hub_version_is_ignored(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            env = empty_env(temp)
            hub_dir = Path(env["APPDATA"]) / "UnityHub"
            hub_dir.mkdir(parents=True)
            install = Path(temp) / "hub" / "2021.3.1f1"
            make_exe(install / "Editor" / "Unity.exe")
            (hub_dir / "editors.json").write_text(
                json.dumps({"editors": [{"version": "2021.3.1f1", "location": str(install)}]}),
                encoding="utf-8",
            )
            with self.assertRaises(UnityPathError):
                resolve_unity_exe(project, None, env, None, False)

    def test_standard_install_root_is_used(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            env = empty_env(temp)
            make_exe(Path(env["ProgramFiles"]) / "Unity" / "Hub" / "Editor" / VERSION / "Editor" / "Unity.exe")
            path, source = resolve_unity_exe(project, None, env, None, False)
        self.assertIn(VERSION, path)
        self.assertIn("standard install", source)

    def test_missing_editor_reports_actionable_chain(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            with self.assertRaises(UnityPathError) as context:
                resolve_unity_exe(project, None, empty_env(temp), None, False)
        message = str(context.exception)
        self.assertIn("UNITY_EXE", message)
        self.assertIn("--unity-exe", message)
        self.assertIn("unity-editor.txt", message)

    def test_explicit_missing_path_fails_without_fallback(self):
        with tempfile.TemporaryDirectory() as temp:
            project = make_project(temp)
            env_exe = make_exe(Path(temp) / "env" / "Unity.exe")
            with self.assertRaises(UnityPathError) as context:
                resolve_unity_exe(project, str(Path(temp) / "missing.exe"), {"UNITY_EXE": str(env_exe)}, None, False)
        self.assertIn("--unity-exe", str(context.exception))


if __name__ == "__main__":
    unittest.main()
