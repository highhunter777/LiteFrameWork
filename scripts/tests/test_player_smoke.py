# -*- coding: utf-8 -*-
"""Offline contracts for the Python Player smoke gate (P4) + Android channel (T5)."""

import importlib.util
import re
import subprocess
import sys
import unittest
from pathlib import Path
from unittest import mock

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


class FakeAdb:
    """纯 Python fake adb：记录命令序列并按预设页返回 logcat。"""

    def __init__(self, abilist="x86_64,arm64-v8a", logcat_pages=None, install_code=0, start_code=0):
        self.abilist = abilist
        self.pages = list(logcat_pages or ["nothing yet"])
        self.install_code = install_code
        self.start_code = start_code
        self.calls = []

    def __call__(self, adb, serial, args, timeout=180):
        self.calls.append(list(args))
        if args[:3] == ["shell", "getprop", "ro.product.cpu.abilist"]:
            return self._result(0, self.abilist + "\n")
        if args[0] == "install":
            return self._result(self.install_code, "Success" if self.install_code == 0 else "Failure [INSTALL_FAILED]")
        if args[0] == "logcat" and args[1] == "-d":
            page = self.pages.pop(0) if len(self.pages) > 1 else self.pages[0]
            return self._result(0, page + "\n")
        if args[0] == "logcat" and args[1] == "-c":
            return self._result(0, "")
        if args[:2] == ["shell", "am"] and "start" in args:
            return self._result(self.start_code, "Starting: Intent")
        if args[0] == "uninstall":
            return self._result(0, "Success")
        return self._result(0, "")

    @staticmethod
    def _result(code, text):
        return type("R", (), {"returncode": code, "stdout": text, "stderr": ""})()


class AndroidSmokeFlowTests(unittest.TestCase):
    """android 通道的流程契约（fake adb：安装/清缓冲/启动/轮询/工件/卸载）。"""

    @classmethod
    def setUpClass(cls):
        cls.mod = load_module("player_smoke_flow", SCRIPT)

    def _run(self, fake, pages=None, extra_args=None, abilist="x86_64,arm64-v8a", install_code=0):
        import tempfile

        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        project = Path(temp.name)
        apk = project / "Test.apk"
        apk.write_bytes(b"apk")
        logs = list(pages or ["[Asset] ready package=DefaultPackage"])
        fake.abilist = abilist
        fake.pages = logs
        fake.install_code = install_code
        artifact = project / "TestResults" / "android-smoke" / "session.log"
        args = self.mod.parse_args([
            "--platform", "android",
            "--apk-path", str(apk),
            "--package-name", "com.litegame.dev",
            "--timeout-sec", "5",
            *(extra_args or []),
        ])
        with mock.patch.object(self.mod, "resolve_adb", return_value="adb"), \
                mock.patch.object(self.mod, "adb_run", fake):
            code = self.mod.run_android_smoke(args, project, artifact)
        return code, fake, artifact

    def test_happy_path_installs_clears_launches_and_uninstalls(self):
        fake = FakeAdb()
        code, fake, artifact = self._run(fake, pages=["booting", "[Asset] ready package=DefaultPackage"])

        self.assertEqual(code, 0)
        commands = [tuple(call) for call in fake.calls]
        self.assertEqual(commands[0], ("shell", "getprop", "ro.product.cpu.abilist"))
        self.assertEqual(commands[1][0], "install")
        self.assertIn("-t", commands[1])
        self.assertIn("-r", commands[1])
        self.assertEqual(commands[2], ("logcat", "-c"))              # 先清缓冲，防上一局残留假绿
        self.assertEqual(commands[3], ("shell", "am", "force-stop", "com.litegame.dev"))
        self.assertEqual(commands[4], ("shell", "am", "start", "-n",
                                       "com.litegame.dev/com.unity3d.player.UnityPlayerActivity"))
        self.assertEqual(commands[-1], ("uninstall", "com.litegame.dev"))
        self.assertTrue(artifact.is_file(), "会话日志工件必须落盘")
        self.assertIn("[Asset] ready", artifact.read_text(encoding="utf-8"))

    def test_bootstrap_error_is_a_failure(self):
        code, _, _ = self._run(FakeAdb(), pages=["BootstrapError 预载失败"])
        self.assertEqual(code, 1)

    def test_no_marker_times_out_as_failure(self):
        code, _, _ = self._run(FakeAdb(), pages=["nothing relevant"])
        self.assertEqual(code, 1)

    def test_keep_installed_skips_uninstall(self):
        code, fake, _ = self._run(FakeAdb(), extra_args=["--keep-installed"])
        self.assertEqual(code, 0)
        self.assertNotIn(("uninstall", "com.litegame.dev"), [tuple(c) for c in fake.calls])

    def test_arm_preflight_blocks_install_without_arm64(self):
        code, fake, _ = self._run(FakeAdb(), abilist="x86_64,x86")
        self.assertEqual(code, 1)
        self.assertEqual([tuple(c) for c in fake.calls],
                         [("shell", "getprop", "ro.product.cpu.abilist")],
                         "无 ARM 翻译时必须 fail-fast，不尝试安装")

    def test_skip_arm_preflight_bypasses_abilist(self):
        code, fake, _ = self._run(FakeAdb(), abilist="x86_64", extra_args=["--skip-arm-preflight"])
        self.assertEqual(code, 0)
        self.assertNotIn(("shell", "getprop", "ro.product.cpu.abilist"), [tuple(c) for c in fake.calls])

    def test_install_failure_fails_and_still_skips_uninstall(self):
        code, fake, _ = self._run(FakeAdb(), install_code=1)
        self.assertEqual(code, 1)
        self.assertNotIn(("uninstall", "com.litegame.dev"), [tuple(c) for c in fake.calls])


if __name__ == "__main__":
    unittest.main()
