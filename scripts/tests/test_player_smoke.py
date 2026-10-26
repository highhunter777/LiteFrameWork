# -*- coding: utf-8 -*-
"""Offline contracts for the Python Player smoke gate (P4) + Android channel (T5)."""

import importlib.util
import re
import subprocess
import sys
import unittest
from pathlib import Path

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build" / "player-smoke.py"


def load_module(name, path):
    if not path.is_file():
        raise AssertionError(f"script missing: {path}")
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PlayerSmokeTests(unittest.TestCase):
    def require_script(self):
        if not SCRIPT.is_file():
            self.fail(f"player-smoke script missing: {SCRIPT}")

    def test_help(self):
        self.require_script()
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--help"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("--exe-path", result.stdout)

    def test_missing_exe_fails(self):
        self.require_script()
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--exe-path", "Builds/no-such.exe"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("exe missing", result.stdout + result.stderr)


class PlayerSmokeAndroidContractTests(unittest.TestCase):
    """T5 契约：android 冒烟通道（参数/寻址/包名/标记判定/日志工件）。

    红测先行：本类在实现前运行应失败（平台参数与辅助函数不存在），实现后转绿。
    """

    @classmethod
    def setUpClass(cls):
        cls.mod = load_module("player_smoke_under_test", SCRIPT)

    def test_parse_args_accepts_android_flags(self):
        args = self.mod.parse_args([
            "--platform", "android",
            "--adb-serial", "127.0.0.1:7555",
            "--package-name", "com.litegame.dev",
            "--timeout-sec", "120",
        ])
        self.assertEqual(args.platform, "android")
        self.assertEqual(args.adb_serial, "127.0.0.1:7555")
        self.assertEqual(args.package_name, "com.litegame.dev")
        self.assertEqual(args.timeout_sec, 120)

    def test_default_platform_is_desktop(self):
        self.assertEqual(self.mod.parse_args([]).platform, "desktop")

    def test_platform_choices_reject_unknown(self):
        with self.assertRaises(SystemExit):
            self.mod.parse_args(["--platform", "ios"])

    def test_adb_serial_args(self):
        self.assertEqual(self.mod.adb_serial_args("emulator-5554"), ["-s", "emulator-5554"])
        self.assertEqual(self.mod.adb_serial_args(None), [])
        self.assertEqual(self.mod.adb_serial_args("  "), [])

    def test_adb_path_from_editor(self):
        self.assertEqual(
            self.mod.adb_path_from_editor("E:/unity3d/2022.3.55f1c1/Editor/Unity.exe"),
            "E:/unity3d/2022.3.55f1c1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe",
        )

    def test_adb_path_from_editor_rejects_non_editor_path(self):
        with self.assertRaises(RuntimeError):
            self.mod.adb_path_from_editor("")

    def test_android_package_name_matches_project_settings(self):
        text = (ROOT / "ProjectSettings" / "ProjectSettings.asset").read_text(encoding="utf-8", errors="replace")
        match = re.search(r"applicationIdentifier:\s*\n\s*Android:\s*(\S+)", text)
        self.assertIsNotNone(match, "ProjectSettings.asset 缺 Android applicationIdentifier")
        self.assertEqual(self.mod.android_package_name(ROOT), match.group(1))

    def test_evaluate_markers_hit_and_miss(self):
        markers = ["BootstrapError", "[Asset] ready"]
        self.assertEqual(self.mod.evaluate_markers("x\n[Asset] ready\ny", markers), "[Asset] ready")
        self.assertIsNone(self.mod.evaluate_markers("nothing here", markers))

    def test_session_log_artifact_path(self):
        path = Path(self.mod.session_log_artifact(ROOT))
        normalized = str(path).replace("\\", "/")
        root = str(ROOT).replace("\\", "/")
        self.assertTrue(normalized.startswith(root + "/TestResults/android-smoke/"), normalized)
        self.assertTrue(path.name.endswith(".log"))

    def test_parse_args_accepts_log_artifact_override(self):
        args = self.mod.parse_args(["--platform", "android", "--log-artifact", "TestResults/custom/x.log"])
        self.assertTrue(str(args.log_artifact).replace("\\", "/").endswith("TestResults/custom/x.log"))

    def test_android_missing_adb_fails_fast(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--platform", "android", "--adb-path", "Builds/no-such-adb.exe"],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("adb", (result.stdout + result.stderr).lower())


if __name__ == "__main__":
    unittest.main()
