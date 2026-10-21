# -*- coding: utf-8 -*-
"""Contracts for the Python nightly gate (P3b, entry surface)."""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "gate" / "nightly-gate.py"


class NightlyEntryTests(unittest.TestCase):
    def test_help(self):
        if not SCRIPT.is_file():
            self.fail(f"nightly script missing: {SCRIPT}")
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
        self.assertIn("--skip-l1", result.stdout)


if __name__ == "__main__":
    unittest.main()
