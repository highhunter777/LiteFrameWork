# -*- coding: utf-8 -*-
"""平台脚本宏期望集校验面（manifest.platformDefines）契约。

期望宏集是"Android 与 Standalone 脚本宏差集逐项裁决"的唯一登记点；
check 读 ProjectSettings.asset 的 scriptingDefineSymbols 比对，缺宏/多宏都算错。
"""

import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

from _toolchain_defines import check_defines, parse_define_symbols, split_defines  # noqa: E402

SETTINGS = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  m_ObjectHideFlags: 0
  productName: Test
  scriptingDefineSymbols:
    Android: DOTWEEN;HOTFIX_ENABLE
    Standalone: ODIN_INSPECTOR;DOTWEEN
  additionalCompilerArguments: []
"""

MANIFEST = {"platformDefines": {"Android": ["DOTWEEN", "HOTFIX_ENABLE"]}}


def write_settings(text):
    handle = tempfile.NamedTemporaryFile("w", suffix=".asset", delete=False, encoding="utf-8")
    handle.write(text)
    handle.close()
    return Path(handle.name)


def check(text, manifest=None, settings=None):
    path = settings or write_settings(text)
    return check_defines(manifest or MANIFEST, ROOT, settings_path=path)


class ParseTests(unittest.TestCase):
    def test_reads_each_platform_line(self):
        symbols = parse_define_symbols(SETTINGS)
        self.assertEqual(symbols["Android"], ["DOTWEEN", "HOTFIX_ENABLE"])
        self.assertEqual(symbols["Standalone"], ["ODIN_INSPECTOR", "DOTWEEN"])

    def test_stops_at_section_end(self):
        text = SETTINGS.replace("  additionalCompilerArguments: []", "  scriptingDefineSymbolsExtra: X")
        symbols = parse_define_symbols(text)
        self.assertNotIn("scriptingDefineSymbolsExtra", symbols)

    def test_inline_empty_map_yields_nothing(self):
        self.assertEqual(parse_define_symbols("  scriptingDefineSymbols: {}\n  next: 1\n"), {})

    def test_missing_section_yields_nothing(self):
        self.assertEqual(parse_define_symbols("PlayerSettings:\n  productName: Test\n"), {})

    def test_split_trims_and_drops_empties(self):
        self.assertEqual(split_defines(" A ; B ;; "), ["A", "B"])
        self.assertEqual(split_defines("{}"), [])


class AlignedTests(unittest.TestCase):
    def test_aligned_set_is_clean(self):
        self.assertEqual(check(SETTINGS), [])

    def test_platform_without_expectation_is_ignored(self):
        # Standalone 在工程设置里但未登记期望集（ODIN 系是编辑器宏，不进平台差集裁决）——不报
        manifest = {"platformDefines": {"Android": ["DOTWEEN", "HOTFIX_ENABLE"]}}
        self.assertEqual(check(SETTINGS.replace("ODIN_INSPECTOR;DOTWEEN", "ODIN_INSPECTOR;ANYTHING"), manifest), [])

    def test_missing_feature_section_is_clean(self):
        self.assertEqual(check(SETTINGS, {"tools": []}), [])

    def test_comment_key_is_not_a_platform(self):
        manifest = {"platformDefines": {"$comment": "说明", "Android": ["DOTWEEN", "HOTFIX_ENABLE"]}}
        self.assertEqual(check(SETTINGS, manifest), [])


class DriftTests(unittest.TestCase):
    def test_missing_macro_is_an_error(self):
        issues = check(SETTINGS.replace("DOTWEEN;HOTFIX_ENABLE", "DOTWEEN"))
        self.assertEqual(len(issues), 1)
        self.assertEqual(issues[0][0], "error")
        self.assertIn("HOTFIX_ENABLE", issues[0][1])

    def test_extra_macro_is_an_error(self):
        issues = check(SETTINGS.replace("DOTWEEN;HOTFIX_ENABLE", "DOTWEEN;HOTFIX_ENABLE;MAGICACLOTH2"))
        self.assertEqual(len(issues), 1)
        self.assertEqual(issues[0][0], "error")
        self.assertIn("MAGICACLOTH2", issues[0][1])

    def test_platform_absent_from_settings_reports_every_macro_missing(self):
        issues = check("  scriptingDefineSymbols:\n    Standalone: DOTWEEN\n")
        self.assertEqual(len(issues), 1)
        self.assertIn("DOTWEEN", issues[0][1])
        self.assertIn("HOTFIX_ENABLE", issues[0][1])

    def test_empty_expectation_is_a_config_error(self):
        issues = check(SETTINGS, {"platformDefines": {"Android": []}})
        self.assertEqual(len(issues), 1)
        self.assertIn("非空", issues[0][1])

    def test_missing_settings_file_is_an_error(self):
        issues = check_defines(MANIFEST, ROOT, settings_path=ROOT / "ProjectSettings" / "NoSuchFile.asset")
        self.assertEqual(len(issues), 1)
        self.assertIn("不存在", issues[0][1])


if __name__ == "__main__":
    unittest.main()
