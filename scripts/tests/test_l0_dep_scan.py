# -*- coding: utf-8 -*-
"""Contracts for the Python L0 dependency/secret scan (P1)."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
L0 = ROOT / "scripts" / "gate" / "l0-dep-scan.py"


class L0ScanTests(unittest.TestCase):
    def require_script(self):
        if not L0.is_file():
            self.fail(f"L0 script missing: {L0}")

    def run_l0(self, project):
        return subprocess.run(
            [sys.executable, str(L0), "--project-path", str(project)],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=120,
        )

    def make_fixture(self, temp):
        root = Path(temp)
        (root / "Packages").mkdir()
        (root / "scripts").mkdir()
        (root / "Packages" / "manifest.json").write_text(
            json.dumps({"dependencies": {}}), encoding="utf-8"
        )
        (root / "Packages" / "packages-lock.json").write_text(
            json.dumps({"dependencies": {}}), encoding="utf-8"
        )
        (root / "scripts" / "toolchain.py").write_text(
            "import sys\nraise SystemExit(0)\n", encoding="utf-8"
        )
        subprocess.run(["git", "init"], cwd=str(root), check=True, capture_output=True)
        self.refresh_git(root)
        return root

    def refresh_git(self, root):
        subprocess.run(["git", "add", "-A"], cwd=str(root), check=True, capture_output=True)

    def test_repo_matches_known_baseline(self):
        self.require_script()
        result = self.run_l0(ROOT)
        if result.returncode == 0:
            self.assertIn("[gate/l0] PASS", result.stdout)
            return
        failures = [line for line in result.stdout.splitlines() if "FAIL" in line]
        self.assertTrue(failures, result.stdout + result.stderr)
        self.assertTrue(
            all("ParrelSync" in line or "L0 scan FAILED" in line for line in failures),
            failures,
        )
        self.assertNotIn("toolchain check failed", result.stdout)

    def test_clean_fixture_passes(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = self.make_fixture(temp)
            result = self.run_l0(root)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_personal_absolute_path_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = self.make_fixture(temp)
            (root / "Packages" / "manifest.json").write_text(
                json.dumps({"dependencies": {"com.bad": "file:C:/secret"}}), encoding="utf-8"
            )
            self.refresh_git(root)
            result = self.run_l0(root)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("personal absolute path", result.stdout)

    def test_floating_git_dependency_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = self.make_fixture(temp)
            (root / "Packages" / "manifest.json").write_text(
                json.dumps({"dependencies": {"com.bad": "https://github.com/x/y.git"}}), encoding="utf-8"
            )
            self.refresh_git(root)
            result = self.run_l0(root)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("floating git dep", result.stdout)

    def test_vendored_package_without_license_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = self.make_fixture(temp)
            package = root / "Packages" / "com.test"
            package.mkdir()
            (package / "package.json").write_text(
                json.dumps({"name": "com.test", "version": "1.0.0"}), encoding="utf-8"
            )
            self.refresh_git(root)
            result = self.run_l0(root)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("no LICENSE", result.stdout)

    def test_secret_pattern_fails(self):
        self.require_script()
        with tempfile.TemporaryDirectory() as temp:
            root = self.make_fixture(temp)
            (root / "secret.txt").write_text(
                'api_key = "abcdefghijklmnopqrstuvwxyz123456"\n', encoding="utf-8"
            )
            self.refresh_git(root)
            result = self.run_l0(root)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("possible secret", result.stdout)


if __name__ == "__main__":
    unittest.main()
