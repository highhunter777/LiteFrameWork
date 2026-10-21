# -*- coding: utf-8 -*-
"""Offline contracts for the Python Player builder (P4)."""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build" / "build-player.py"


class BuildPlayerTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"build-player script missing: {SCRIPT}")

    def test_help(self):
        self.require_script()
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--help"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--profile", result.stdout)

    def test_missing_profile_fails(self):
        self.require_script()
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--profile", "BuildProfiles/no-such.json"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("profile missing", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
