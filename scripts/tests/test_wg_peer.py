# -*- coding: utf-8 -*-
"""Offline contracts for the WireGuard peer entry (P5)."""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "deploy" / "wg-peer.py"


class WgPeerTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"wg-peer script missing: {SCRIPT}")

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
        self.assertIn("--name", result.stdout)

    def test_missing_name_is_usage_error(self):
        self.require_script()
        result = self.run_script()
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)

    def test_invalid_name_is_usage_error(self):
        self.require_script()
        result = self.run_script("--name", "bad/name")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
