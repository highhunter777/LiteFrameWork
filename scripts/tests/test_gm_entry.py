# -*- coding: utf-8 -*-
"""Offline contracts for the cloud GM entry (P5)."""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "gm.py"


class GmEntryTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"gm script missing: {SCRIPT}")

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
        self.assertIn("--action", result.stdout)
        self.assertIn("--file", result.stdout)

    def test_usage_lists_contract_actions(self):
        self.require_script()
        result = self.run_script()
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        for action in ("rooms", "diag-list", "diag-fetch", "settle-flush"):
            self.assertIn(action, result.stdout)

    def test_unknown_action_is_usage_error(self):
        self.require_script()
        result = self.run_script("--action", "bogus")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)

    def test_missing_room_is_usage_error(self):
        self.require_script()
        result = self.run_script("--action", "start")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)

    def test_diag_fetch_without_file_is_usage_error(self):
        self.require_script()
        result = self.run_script("--action", "diag-fetch")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("--file", result.stdout)


if __name__ == "__main__":
    unittest.main()
