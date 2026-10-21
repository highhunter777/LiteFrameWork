# -*- coding: utf-8 -*-
"""Contract tests for newly registered script entries.

These tests only use help/usage paths; they never execute deployment bodies or
touch cloud state.
"""

import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def run(args, timeout=60):
    return subprocess.run(
        args,
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        stdin=subprocess.DEVNULL,
        timeout=timeout,
    )


def run_script(relative, *args, timeout=60):
    return run([sys.executable, str(ROOT / relative), *args], timeout=timeout)


class LocalChainContractTests(unittest.TestCase):
    def test_unknown_command_is_usage_error(self):
        result = run_script("scripts/local/local-chain.py", "no-such-command")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("init", result.stdout.lower())

    def test_help_lists_commands(self):
        result = run_script("scripts/local/local-chain.py", "--help")
        self.assertEqual(result.returncode, 0, result.stderr)
        text = result.stdout.lower()
        for command in ("init", "meta", "room", "up", "status", "keys"):
            self.assertIn(command, text)


class DeployContractTests(unittest.TestCase):
    def test_help_has_canonical_options(self):
        expected = {
            "scripts/deploy/gm.py": "--action",
            "scripts/deploy/wg-peer.py": "--name",
            "scripts/deploy/deploy-room.py": "--skip-publish",
        }
        for relative, keyword in expected.items():
            result = run_script(relative, "--help")
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(keyword, result.stdout, f"{relative} help missing {keyword}")

    def test_gm_missing_action_is_usage_error(self):
        result = run_script("scripts/deploy/gm.py")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)

    def test_wg_peer_missing_name_is_usage_error(self):
        result = run_script("scripts/deploy/wg-peer.py")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)


class PythonEntryContractTests(unittest.TestCase):
    def test_doc_lint_help(self):
        result = run([sys.executable, "scripts/gate/doc-lint.py", "--help"])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("usage", result.stdout.lower())

    def test_scan_missing_guids_help(self):
        result = run([sys.executable, "scripts/content/scan_missing_guids.py", "--help"], timeout=120)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("usage", result.stdout.lower())


if __name__ == "__main__":
    unittest.main()
