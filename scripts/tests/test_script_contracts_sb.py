# -*- coding: utf-8 -*-
"""S-B contracts: native help, usage fail-fast and local exit codes."""

import contextlib
import importlib
import io
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "scripts" / "gate" / "toolchain.json"
SCRIPTS = ROOT / "scripts"


def run_script(relative, *args, timeout=120):
    return subprocess.run(
        [sys.executable, str(ROOT / relative), *args],
        cwd=str(ROOT),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        stdin=subprocess.DEVNULL,
        timeout=timeout,
    )


def manifest_scripts():
    import json

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    scripts = set()
    for tool in manifest["tools"]:
        command = tool.get("command") or []
        if not command or command[0] not in {"python", "python3"}:
            continue
        for token in command:
            if token.startswith("scripts/") and token.endswith(".py"):
                scripts.add(token)
    return sorted(scripts)


class HelpContractTests(unittest.TestCase):
    def test_every_registered_python_entry_has_help(self):
        scripts = manifest_scripts()
        self.assertTrue(scripts)
        for relative in scripts:
            with self.subTest(script=relative):
                result = run_script(relative, "--help")
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                text = (result.stdout + result.stderr).lower()
                self.assertTrue("usage" in text or "toolbox" in text, f"{relative} help is empty")

    def test_direct_generator_entries_have_help(self):
        for relative in (
            "scripts/codegen/gen-build-hash.py",
            "scripts/codegen/gen_luban.py",
            "scripts/codegen/gen_proto.py",
            "scripts/codegen/gen_sync_code.py",
            "scripts/codegen/refresh-hash.py",
            "scripts/codegen/toolbox.py",
        ):
            with self.subTest(script=relative):
                result = run_script(relative, "--help")
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn("usage", (result.stdout + result.stderr).lower())

    def test_hook_has_help(self):
        result = run_script("scripts/hooks/shared-source-reminder.py", "--help")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("PostToolUse", result.stdout)

    def test_domain_entries_without_args_print_usage_and_succeed(self):
        for relative in ("scripts/codegen/toolbox.py", "scripts/local/local-chain.py"):
            with self.subTest(script=relative):
                result = run_script(relative)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn("usage", (result.stdout + result.stderr).lower())


class UsageFailFastTests(unittest.TestCase):
    def test_generator_unknown_option_is_usage_error(self):
        for relative in ("scripts/codegen/gen-build-hash.py", "scripts/codegen/gen_sync_code.py"):
            with self.subTest(script=relative):
                result = run_script(relative, "--bogus")
                self.assertEqual(result.returncode, 2, result.stdout + result.stderr)

    def test_toolbox_unknown_tool_is_usage_error(self):
        result = run_script("scripts/codegen/toolbox.py", "no-such-tool")
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("build-hash", result.stdout)

    def test_native_dry_run_flag_is_documented(self):
        result = run_script("scripts/deploy/wg-peer.py", "--help")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("--dry-run", result.stdout)

    def test_manifest_level_dry_run_gate_is_explicit(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPTS / "toolchain.py"), "run", "deploy.wg-peer", "--dry-run"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdin=subprocess.DEVNULL,
            timeout=60,
        )
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn("does not support run --dry-run", result.stderr)


class ExitCodeTests(unittest.TestCase):
    def test_toolbox_normalizes_child_business_code(self):
        sys.path.insert(0, str(SCRIPTS / "codegen"))
        toolbox = importlib.import_module("toolbox")
        with tempfile.TemporaryDirectory() as temp:
            child = Path(temp) / "child.py"
            child.write_text("import sys\nsys.exit(3)\n", encoding="utf-8")
            err = io.StringIO()
            with mock.patch.dict(toolbox.TOOLS, {"fixture": (str(child), "", "")}), \
                 mock.patch.object(sys, "argv", ["toolbox.py", "fixture"]), \
                 contextlib.redirect_stderr(err):
                code = toolbox.main()
        self.assertEqual(code, 1)
        self.assertIn("3", err.getvalue())

    def test_scripts_do_not_emit_business_exit_codes(self):
        pattern = re.compile(r"(?:sys\.exit|SystemExit)\(\s*([3-9]\d*)\s*\)")
        offenders = []
        for path in sorted(SCRIPTS.rglob("*.py")):
            relative = path.relative_to(ROOT).as_posix()
            if "/tests/" in relative or relative == "scripts/toolchain.py":
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            if pattern.search(text):
                offenders.append(relative)
        self.assertEqual(offenders, [])


class OutputPrefixTests(unittest.TestCase):
    def test_install_nightly_uses_domain_prefix(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPTS / "gate" / "install-nightly-task.py"), "--dry-run"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdin=subprocess.DEVNULL,
            timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("[env/nightly-install]", result.stdout)
        self.assertIn("WhatIf", result.stdout)

    def test_restore_check_uses_domain_prefix(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPTS / "gate" / "restore-check.py")],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            stdin=subprocess.DEVNULL,
            timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("[env/restore]", result.stdout)
        self.assertIn("Restore check PASSED", result.stdout)


if __name__ == "__main__":
    unittest.main()
