# -*- coding: utf-8 -*-
"""S-C2 contracts: repository root and output helpers have one implementation."""

import contextlib
import importlib
import io
import re
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
sys.path.insert(0, str(SCRIPTS))
sys.path.insert(0, str(SCRIPTS / "codegen"))

common = importlib.import_module("_common")
codegen = importlib.import_module("_codegen")
toolchain_manifest = importlib.import_module("_toolchain_manifest")


class RootSourceTests(unittest.TestCase):
    def test_codegen_root_delegates_to_common(self):
        self.assertEqual(Path(codegen.repo_root()).resolve(), common.repo_root().resolve())

    def test_toolchain_manifest_reuses_common_root(self):
        self.assertIs(toolchain_manifest.repo_root, common.repo_root)

    def test_no_duplicate_root_derivation_in_product_scripts(self):
        patterns = (
            re.compile(r"ROOT\s*=\s*os\.path\.abspath\(os\.path\.join\(os\.path\.dirname\(__file__\)"),
            re.compile(r"Path\(__file__\)\.resolve\(\)\.parents\[2\]"),
            re.compile(r"return\s+Path\(__file__\)\.resolve\(\)\.parent\.parent"),
            re.compile(r"return\s+os\.path\.dirname\(os\.path\.dirname\(os\.path\.dirname\("),
        )
        offenders = []
        for path in sorted(SCRIPTS.rglob("*.py")):
            relative = path.relative_to(ROOT).as_posix()
            if "/tests/" in relative or relative == "scripts/_common.py":
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            if any(pattern.search(text) for pattern in patterns):
                offenders.append(relative)
        self.assertEqual(offenders, [])


class OutputHelperTests(unittest.TestCase):
    def capture(self, func, *args):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = func(*args)
        return code, out.getvalue(), err.getvalue()

    def test_info_and_warn_match_common(self):
        for name in ("info", "warn"):
            with self.subTest(helper=name):
                codegen_result = self.capture(getattr(codegen, name), "probe", "message")
                common_result = self.capture(getattr(common, name), "codegen/probe", "message")
                self.assertEqual(codegen_result, common_result)

    def test_fail_matches_common_and_returns_failure(self):
        codegen_result = self.capture(codegen.fail, "probe", "message")
        common_result = self.capture(common.fail, "codegen/probe", "message")
        self.assertEqual(codegen_result, common_result)
        self.assertEqual(codegen_result[0], common.EXIT_FAIL)


if __name__ == "__main__":
    unittest.main()
