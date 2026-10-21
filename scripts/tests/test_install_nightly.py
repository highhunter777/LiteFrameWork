# -*- coding: utf-8 -*-
"""Contracts for the Python nightly task installer (P3b, what-if only)."""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "gate" / "install-nightly-task.py"


class InstallNightlyTests(unittest.TestCase):
    def test_what_if_prints_python_action(self):
        if not SCRIPT.is_file():
            self.fail(f"installer missing: {SCRIPT}")
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--what-if"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("nightly-gate.py", result.stdout)
        self.assertIn("python", result.stdout.lower())
        self.assertIn("WhatIf", result.stdout)


if __name__ == "__main__":
    unittest.main()
