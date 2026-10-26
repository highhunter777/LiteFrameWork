# -*- coding: utf-8 -*-
"""T1 契约：构建链平台参数化（profile → Unity 构建参数/产物/内容包门禁）。

红测先行：本文件在实现前运行应失败（映射函数不存在），实现后转绿。
覆盖：Android/Windows 双档映射、未知平台拒绝、artifactGlob 缺省、
后端切换脚本、内置内容包平台白名单、toolchain build 入口参数。
"""

import importlib.util
import json
import sys
import unittest
from pathlib import Path

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
BUILD_PLAYER = ROOT / "scripts" / "build" / "build-player.py"
TOOLCHAIN = ROOT / "scripts" / "toolchain.py"


def load_module(name, path):
    if not path.is_file():
        raise AssertionError(f"script missing: {path}")
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_profile(relative):
    return json.loads((ROOT / relative).read_text(encoding="utf-8"))


ANDROID = load_profile("BuildProfiles/android-il2cpp.json")
WINDOWS = load_profile("BuildProfiles/windows-x64.json")


class BuildPlayerPlatformContractTests(unittest.TestCase):
    """build-player.py 的平台映射契约（纯函数——不含 Unity 调用）。"""

    @classmethod
    def setUpClass(cls):
        cls.mod = load_module("build_player_under_test", BUILD_PLAYER)

    def test_android_profile_named_target(self):
        self.assertEqual(self.mod.named_build_target(ANDROID), "Android")

    def test_windows_profile_named_target(self):
        self.assertEqual(self.mod.named_build_target(WINDOWS), "Standalone")

    def test_unknown_platform_rejected(self):
        with self.assertRaises(RuntimeError):
            self.mod.named_build_target({"platform": "WebGL"})

    def test_explicit_build_target_group_wins(self):
        self.assertEqual(
            self.mod.named_build_target({"platform": "Android", "buildTargetGroup": "Android"}),
            "Android",
        )

    def test_android_player_extension_is_apk(self):
        self.assertEqual(self.mod.player_extension(ANDROID), ".apk")

    def test_windows_player_extension_is_exe(self):
        self.assertEqual(self.mod.player_extension(WINDOWS), ".exe")

    def test_unknown_platform_extension_rejected(self):
        with self.assertRaises(RuntimeError):
            self.mod.player_extension({"platform": "WebGL"})

    def test_artifact_glob_prefers_profile_value(self):
        self.assertEqual(self.mod.artifact_glob(ANDROID), "*.apk")
        self.assertEqual(self.mod.artifact_glob(WINDOWS), "*.exe")

    def test_artifact_glob_defaults_by_platform(self):
        self.assertEqual(self.mod.artifact_glob({"platform": "Android"}), "*.apk")
        self.assertEqual(self.mod.artifact_glob({"platform": "StandaloneWindows64"}), "*.exe")

    def test_bundles_supported_platforms(self):
        self.assertTrue(self.mod.bundles_supported(ANDROID))
        self.assertTrue(self.mod.bundles_supported(WINDOWS))
        self.assertFalse(self.mod.bundles_supported({"platform": "WebGL"}))

    def test_backend_switch_android_uses_android_named_target(self):
        code = self.mod.backend_switch_cs(ANDROID)
        self.assertIn("NamedBuildTarget.Android", code)
        self.assertIn("ScriptingImplementation.IL2CPP", code)
        self.assertNotIn("NamedBuildTarget.Standalone", code)

    def test_backend_switch_windows_keeps_standalone(self):
        code = self.mod.backend_switch_cs(WINDOWS)
        self.assertIn("NamedBuildTarget.Standalone", code)
        self.assertIn("ScriptingImplementation.IL2CPP", code)

    def test_backend_switch_honors_mono_profile(self):
        code = self.mod.backend_switch_cs({"platform": "Android", "backend": "Mono"})
        self.assertIn("ScriptingImplementation.Mono", code)

    def test_output_expr_android_ends_with_apk(self):
        expr = self.mod.build_output_expr(ANDROID)
        self.assertIn('".apk"', expr)
        self.assertIn(ANDROID["output"], expr)

    def test_output_expr_windows_ends_with_exe(self):
        expr = self.mod.build_output_expr(WINDOWS)
        self.assertIn('".exe"', expr)

    def test_bundles_cs_targets_profile_platform(self):
        self.assertIn("BuildTarget.Android", self.mod.bundles_cs(ANDROID))
        self.assertIn("BuildTarget.StandaloneWindows64", self.mod.bundles_cs(WINDOWS))

    def test_profiles_carry_platform_contract_fields(self):
        for profile in (ANDROID, WINDOWS):
            for key in ("platform", "buildTargetGroup", "backend", "output"):
                self.assertIn(key, profile)
        self.assertEqual(ANDROID["platform"], "Android")
        self.assertEqual(ANDROID["artifactGlob"], "*.apk")
        self.assertEqual(WINDOWS["platform"], "StandaloneWindows64")


class ToolchainBuildCommandTests(unittest.TestCase):
    """toolchain.py build 入口参数契约（-Profile 单横线 bug 的回归钉）。"""

    @classmethod
    def setUpClass(cls):
        cls.mod = load_module("toolchain_under_test", TOOLCHAIN)

    def test_build_command_uses_double_dash_profile(self):
        command = self.mod.build_command("BuildProfiles/android-il2cpp.json")
        self.assertIn("--profile", command)
        self.assertNotIn("-Profile", command)
        index = command.index("--profile")
        self.assertEqual(command[index + 1], "BuildProfiles/android-il2cpp.json")
        self.assertTrue(command[1].endswith("build-player.py"))

    def test_build_command_without_profile(self):
        command = self.mod.build_command(None)
        self.assertNotIn("--profile", command)


if __name__ == "__main__":
    unittest.main()
