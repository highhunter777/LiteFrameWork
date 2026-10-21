# -*- coding: utf-8 -*-
"""Offline contracts for the Python Player smoke gate (P4)."""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build" / "player-smoke.py"


class PlayerSmokeTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"player-smoke script missing: {SCRIPT}")

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
        self.assertIn("--exe-path", result.stdout)

    def test_missing_exe_fails(self):
        self.require_script()
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--exe-path", "Builds/no-such.exe"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("exe missing", result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
