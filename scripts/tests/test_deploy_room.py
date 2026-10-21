# -*- coding: utf-8 -*-
"""Offline contracts for the room deployment entry (P5)."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "deploy-room.py"


class DeployRoomTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"deploy-room script missing: {SCRIPT}")

    def run_script(self, *args):
        return subprocess.run(
            [sys.executable, str(SCRIPT), *args],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )

    def test_help(self):
        self.require_script()
        result = self.run_script("--help")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--skip-publish", result.stdout)

    def test_skip_publish_without_artifacts_fails_before_ssh(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            result = self.run_script("--project-path", temp, "--skip-publish")
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("room", (result.stdout + result.stderr).lower())


if __name__ == "__main__":
    unittest.main()
