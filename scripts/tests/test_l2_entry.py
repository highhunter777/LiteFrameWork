# -*- coding: utf-8 -*-
"""Contracts for the Python L2 Unity gate (P3b, offline surface)."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "gate" / "l2-unity-gate.py"


class L2EntryTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"L2 script missing: {SCRIPT}")

    def run_l2(self, *args):
        return subprocess.run(
            [sys.executable, str(SCRIPT), *args],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
        )

    def test_help(self):
        self.require_script()
        result = self.run_l2("--help")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--meta-scan-only", result.stdout)

    def test_repo_meta_scan_passes(self):
        self.require_script()
        result = self.run_l2("--meta-scan-only")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("L2(meta)", result.stdout)
        summary = json.loads((ROOT / "TestResults" / "summary-l2.json").read_text(encoding="utf-8"))
        self.assertEqual(summary["suite"], "L2")
        self.assertEqual(summary["status"], "pass")

    def test_bad_meta_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            (root / "Assets").mkdir()
            (root / "Assets" / "Bad.meta").write_text("guid: not-a-guid\n", encoding="utf-8")
            result = self.run_l2("--project-path", str(root), "--meta-scan-only")
            summary = json.loads((root / "TestResults" / "summary-l2.json").read_text(encoding="utf-8"))
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("非法 GUID", result.stdout)
        self.assertEqual(summary["status"], "fail")

    def test_missing_editor_fails_fast(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "project"
            settings = root / "ProjectSettings"
            settings.mkdir(parents=True)
            (root / "Assets").mkdir()
            (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 2099.1.1f1\n", encoding="utf-8")
            result = self.run_l2(
                "--project-path", str(root), "--run-editmode-tests",
                "--unity-exe", str(Path(temp) / "missing.exe"),
            )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("--unity-exe", result.stdout + result.stderr)
        self.assertIn("missing Unity.exe", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
