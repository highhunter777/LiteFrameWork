# -*- coding: utf-8 -*-
"""Contracts for the Python gate test entry (P3a)."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "gate" / "test.py"


class TestEntryTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"test entry missing: {SCRIPT}")

    def run_test(self, *args):
        return subprocess.run(
            [sys.executable, str(SCRIPT), *args],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=300,
        )

    def test_help(self):
        self.require_script()
        result = self.run_test("--help")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--suite", result.stdout)
        self.assertIn("--profile", result.stdout)

    def test_invalid_suite_is_usage_error(self):
        self.require_script()
        result = self.run_test("--suite", "BOGUS")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)

    def test_doc_suite(self):
        self.require_script()
        result = self.run_test("--suite", "Doc")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("doc-lint", result.stdout.lower())
        summary = json.loads((ROOT / "TestResults" / "summary-doc.json").read_text(encoding="utf-8"))
        self.assertEqual(summary["suite"], "Doc")
        self.assertEqual(summary["status"], "pass")

    def test_discovery_contract_guard(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            (root / "Tests").mkdir()
            (root / "Tests" / "Bad.csproj").write_text("<Project />\n", encoding="utf-8")
            result = subprocess.run(
                [sys.executable, str(SCRIPT), "--suite", "L1", "--project-path", str(root)],
                cwd=str(ROOT),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=60,
            )
            summary = json.loads((root / "TestResults" / "summary-l1.json").read_text(encoding="utf-8"))
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("discovery contract", result.stdout + result.stderr)
        self.assertEqual(summary["status"], "fail")
        self.assertIn("discovery contract", summary["error"])


if __name__ == "__main__":
    unittest.main()
