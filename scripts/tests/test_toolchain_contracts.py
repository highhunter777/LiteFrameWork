# -*- coding: utf-8 -*-
"""Contract tests for newly registered script entries.

These tests only use help/usage paths; they never execute deployment bodies or
touch cloud state.

Run with:
  python -m unittest discover -s scripts/tests -p "test_toolchain*.py" -v
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


def run_pwsh_script(relative, *args, timeout=60):
    return run(["pwsh", "-NoProfile", "-NonInteractive", "-File", str(ROOT / relative), *args], timeout=timeout)


def get_help(relative):
    command = (
        "[Console]::OutputEncoding=[Text.Encoding]::UTF8; "
        f"Get-Help (Resolve-Path '{relative}') | Out-String"
    )
    return run(["pwsh", "-NoProfile", "-Command", command])


class LocalChainContractTests(unittest.TestCase):
    def test_unknown_command_is_usage_error(self):
        result = run_pwsh_script("scripts/local/local-chain.ps1", "no-such-command")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("init", result.stdout.lower())

    def test_help_lists_commands(self):
        result = get_help("scripts/local/local-chain.ps1")
        self.assertEqual(result.returncode, 0, result.stderr)
        text = result.stdout.lower()
        for command in ("init", "meta", "room", "up", "status", "keys"):
            self.assertIn(command, text)


class DeployContractTests(unittest.TestCase):
    def test_help_has_description(self):
        expected = {
            "scripts/deploy/gm.ps1": "云端运维",
            "scripts/deploy/wg-peer.ps1": "测试者",
            "scripts/deploy/deploy-room.ps1": "重新部署",
        }
        for relative, keyword in expected.items():
            result = get_help(relative)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(keyword, result.stdout, f"{relative} help missing description")

    def test_gm_missing_action_is_usage_error(self):
        result = run_pwsh_script("scripts/deploy/gm.ps1")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)

    def test_wg_peer_missing_name_is_usage_error(self):
        result = run_pwsh_script("scripts/deploy/wg-peer.ps1")
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
