# -*- coding: utf-8 -*-
"""Contracts for the shared Python helper (P0 foundation)."""

import contextlib
import importlib
import io
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def load_common():
    sys.path.insert(0, str(ROOT / "scripts"))
    try:
        return importlib.import_module("_common")
    except ImportError as exc:
        raise AssertionError(f"_common missing: {exc}") from exc


class CommonTests(unittest.TestCase):
    def test_repo_root_points_at_repository(self):
        common = load_common()
        self.assertEqual(common.repo_root().resolve(), ROOT.resolve())
        self.assertTrue((common.repo_root() / "AGENTS.md").is_file())

    def test_exit_codes(self):
        common = load_common()
        self.assertEqual(common.EXIT_OK, 0)
        self.assertEqual(common.EXIT_FAIL, 1)
        self.assertEqual(common.EXIT_USAGE, 2)
        self.assertEqual(common.EXIT_NOT_FOUND, 4)
        self.assertEqual(common.EXIT_EDITOR_UNAVAILABLE, 5)

    def test_run_returns_child_exit_code(self):
        common = load_common()
        result = common.run([sys.executable, "-c", "raise SystemExit(3)"])
        self.assertEqual(result.returncode, 3)

    def test_output_helpers(self):
        common = load_common()
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            common.info("gate/test", "ok")
            common.warn("gate/test", "careful")
            code = common.fail("gate/test", "bad")
        self.assertIn("[gate/test] ok", out.getvalue())
        self.assertIn("[gate/test] WARN: careful", err.getvalue())
        self.assertIn("[gate/test] FAILED: bad", err.getvalue())
        self.assertEqual(code, common.EXIT_FAIL)

    def test_configure_stdio_is_safe(self):
        common = load_common()
        common.configure_stdio()


if __name__ == "__main__":
    unittest.main()
