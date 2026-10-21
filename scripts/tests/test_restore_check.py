# -*- coding: utf-8 -*-
"""Contracts for the Python environment restore check (P2)."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "gate" / "restore-check.py"


class RestoreCheckTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"restore-check script missing: {SCRIPT}")

    def run_check(self, project=None):
        command = [sys.executable, str(SCRIPT)]
        if project is not None:
            command.extend(["--project-path", str(project)])
        return subprocess.run(
            command,
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=180,
        )

    def test_repo_passes(self):
        self.require_script()
        result = self.run_check()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Restore check PASSED", result.stdout)

    def test_empty_project_is_blocking(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            result = self.run_check(Path(temp))
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("[MISS]", result.stdout)
        self.assertIn("Restore check FAILED", result.stdout)


if __name__ == "__main__":
    unittest.main()
