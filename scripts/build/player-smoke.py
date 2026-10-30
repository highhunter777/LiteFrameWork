# -*- coding: utf-8 -*-
"""Player startup smoke: launch the built Player and wait for a defined-state marker.

两条通道：
- `desktop`：直接起 exe，读 `LocalLow/<company>/<product>/Player.log`；
- `android`：`adb install + am start + logcat`（编辑器自带 platform-tools，无系统依赖）。

两条通道**共用同一份标记表**（`MARKERS`）——标记定义只此一处，避免"桌面判据改了安卓没跟上"。
桌面语义＝"到达定义态即通过"（错误 UI 也算到达）；安卓增判：`BootstrapError` 视为失败
（安卓包的内容是内置的，出现错误 UI 就是真问题，不是可接受的定义态）。
"""

import argparse
import re
import shutil
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402

# 定义态标记（两条通道的唯一来源）。
MARKERS = ["BootstrapError", "[Asset] ready"]
FAILED_MARKER = "BootstrapError"
GOOD_MARKER = "[Asset] ready"

# 安卓主 Activity（自定义主清单里声明的那一个）。
ANDROID_ACTIVITY = "com.unity3d.player.UnityPlayerActivity"

# 本仓库的 Android .so（xLua）只有 arm64-v8a/armeabi-v7a/x86 三份 ABI；
# x86_64 模拟器必须开 ARM 翻译才能跑（abilist 里会带出 arm64-v8a）。
ANDROID_REQUIRED_ABI = "arm64-v8a"

# 会话日志工件目录（AC5/AC6 引用同一工件，减少三处各写 adb 参数的漂移）。
ANDROID_LOG_DIR = Path("TestResults") / "android-smoke"

DEFAULT_TIMEOUT_SEC = 30
ANDROID_DEFAULT_TIMEOUT_SEC = 120   # 安装 + 首启 + 内置包装载比桌面慢


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Player startup smoke")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--platform", choices=("desktop", "android"), default="desktop")
    parser.add_argument("--exe-path", default="Builds/StandaloneWindows64/Test.exe")
    parser.add_argument("--apk-path", default="Builds/android/Test.apk")
    parser.add_argument("--adb-path", default=None, help="adb 可执行文件（默认取 PATH 上的 adb）")
    parser.add_argument("--adb-serial", default=None, help="设备序列号（-s <serial>；多设备时必填）")
    parser.add_argument("--package-name", default=None, help="安卓包名（默认读 ProjectSettings）")
    parser.add_argument("--log-artifact", default=None, help="会话日志导出路径（默认 TestResults/android-smoke/）")
    parser.add_argument("--keep-installed", action="store_true", help="冒烟后不卸载（默认卸载清理）")
    parser.add_argument("--skip-arm-preflight", action="store_true", help="跳过 ARM 翻译预检（不推荐）")
    parser.add_argument("--timeout-sec", type=int, default=DEFAULT_TIMEOUT_SEC)
    parser.add_argument("--player-arg", action="append", default=[])
    parser.add_argument("--mem-loop", action="store_true")
    parser.add_argument("--mem-loop-cycles", type=int, default=30)
    parser.add_argument("--mem-loop-max-growth-mb", type=float, default=8.0)
    parser.add_argument("--input-modal", action="store_true")
    return parser.parse_args(argv)


# ---- 安卓通道辅助（纯函数 + 可注入 adb 执行器，便于 fake adb 单测） ----


def adb_serial_args(serial):
    """设备寻址：`-s <serial>`；空/空白 = 不指定（单设备）。"""
    text = (serial or "").strip()
    return ["-s", text] if text else []


def adb_path_from_editor(editor_exe_path):
    """编辑器自带 platform-tools 的 adb 路径（与 Unity 安装版本绑定，不依赖系统 PATH）。"""
    if not editor_exe_path:
        raise RuntimeError("editor executable path is empty - cannot locate bundled adb")
    editor = Path(editor_exe_path)
    return (
        editor.parent / "Data" / "PlaybackEngines" / "AndroidPlayer" / "SDK" / "platform-tools" / "adb.exe"
    ).as_posix()


def android_package_name(project):
    """读 ProjectSettings 的 Android applicationIdentifier（工程设置为唯一真源）。"""
    settings = Path(project) / "ProjectSettings" / "ProjectSettings.asset"
    text = settings.read_text(encoding="utf-8", errors="replace")
    match = re.search(r"applicationIdentifier:\s*\n\s*Android:\s*(\S+)", text)
    return match.group(1) if match else ""


def evaluate_markers(log_text, markers):
    """日志文本 → 命中的第一个标记（未命中返回 None）。"""
    for marker in markers:
        if marker in (log_text or ""):
            return marker
    return None


def session_log_artifact(project, override=None):
    """会话日志工件的落地路径（默认按时间戳命名，AC5/AC6 共用）。"""
    if override:
        path = Path(override)
        if not path.is_absolute():
            path = Path(project) / path
        return path
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    return Path(project) / ANDROID_LOG_DIR / f"android-smoke-{stamp}.log"


def abilist_supports_arm64(abilist_text):
    """设备 ABI 列表是否含 arm64-v8a（x86_64 模拟器靠 ARM 翻译时也会带出这一项）。"""
    return ANDROID_REQUIRED_ABI in (abilist_text or "")


def adb_run(adb, serial, args, timeout=180):
    """执行一条 adb 子命令（返回 CompletedProcess；非零退出由调用方判定）。"""
    command = [adb, *adb_serial_args(serial), *args]
    return subprocess.run(
        command,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


def _combined(result):
    return ((result.stdout or "") + (result.stderr or "")).strip()


def resolve_adb(args):
    if args.adb_path:
        path = Path(args.adb_path)
        if not path.is_file():
            raise RuntimeError(f"adb not found: {path}")
        return str(path)
    found = shutil.which("adb")
    if not found:
        raise RuntimeError(
            "adb not found - pass --adb-path (editor bundled: <editor>/Data/PlaybackEngines/"
            "AndroidPlayer/SDK/platform-tools/adb.exe) or put adb on PATH"
        )
    return found


class AndroidSmoke:
    """安卓启动冒烟（adb 执行器与时钟可注入——fake adb 单测不碰真设备）。"""

    def __init__(self, adb, serial, package, apk, run=None, sleep=time.sleep, now=time.monotonic):
        self.adb = adb
        self.serial = serial
        self.package = package
        self.apk = apk
        self._run = run or adb_run          # 运行时解析——用例可 patch 模块级 adb_run
        self._sleep = sleep
        self._now = now
        self.commands = []

    def call(self, args, timeout=180):
        self.commands.append(list(args))
        return self._run(self.adb, self.serial, args, timeout)

    def abilist(self):
        return _combined(self.call(["shell", "getprop", "ro.product.cpu.abilist"]))

    def install(self):
        return self.call(["install", "-t", "-r", str(self.apk)], timeout=600)

    def clear_logcat(self):
        # 先清缓冲：上一局的残留标记会造成假绿（等价于 `-T` 只取新行）。
        return self.call(["logcat", "-c"])

    def stop_app(self):
        return self.call(["shell", "am", "force-stop", self.package])

    def start_app(self):
        return self.call(["shell", "am", "start", "-n", f"{self.package}/{ANDROID_ACTIVITY}"])

    def dump_logcat(self):
        return _combined(self.call(["logcat", "-d"], timeout=120))

    def uninstall(self):
        return self.call(["uninstall", self.package], timeout=300)

    def wait_for_marker(self, timeout_sec):
        """轮询 logcat（启动前已清缓冲 → 缓冲里只有本次会话的行）。"""
        deadline = self._now() + timeout_sec
        log_text = ""
        while self._now() < deadline:
            self._sleep(2)
            log_text = self.dump_logcat()
            if evaluate_markers(log_text, MARKERS):
                return log_text
        return log_text


def run_android_smoke(args, project, log_path):
    adb = resolve_adb(args)
    package = args.package_name or android_package_name(project)
    if not package:
        return fail("build/smoke", "android package name unresolved (pass --package-name)")
    apk = Path(args.apk_path)
    if not apk.is_absolute():
        apk = project / apk
    if not apk.is_file():
        return fail("build/smoke", f"apk missing: {apk} (run build-player.py --profile BuildProfiles/android-il2cpp.json)")

    channel = AndroidSmoke(adb, args.adb_serial, package, apk)
    timeout = args.timeout_sec
    if timeout == DEFAULT_TIMEOUT_SEC:
        timeout = ANDROID_DEFAULT_TIMEOUT_SEC
    info("build/smoke", f"Player smoke (android) | apk: {apk} | adb: {adb} | package: {package}")
    info("build/smoke", f"session log artifact: {log_path}")

    if not args.skip_arm_preflight:
        abilist = channel.abilist()
        if not abilist_supports_arm64(abilist):
            return fail(
                "build/smoke",
                f"device abi list has no {ANDROID_REQUIRED_ABI} (got '{abilist}') - xLua ships arm64/x86 only; "
                "enable ARM translation in the emulator image or use another device",
            )
        info("build/smoke", f"arm preflight OK (abilist: {abilist})")

    installed = False
    try:
        install = channel.install()
        if install.returncode != 0:
            return fail("build/smoke", f"adb install failed: {_combined(install)}")
        installed = True

        channel.clear_logcat()
        channel.stop_app()
        start = channel.start_app()
        if start.returncode != 0:
            return fail("build/smoke", f"adb am start failed: {_combined(start)}")

        info("build/smoke", f"launched; polling logcat up to {timeout}s")
        log_text = channel.wait_for_marker(timeout)
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_path.write_text(log_text, encoding="utf-8", newline="\n")
        info("build/smoke", f"session log written: {log_path}")

        hit = evaluate_markers(log_text, MARKERS)
        if hit is None:
            info("build/smoke", "FAIL: no defined-state marker within timeout - last log lines:")
            for line in log_text.splitlines()[-40:]:
                print(f"    {line}")
            return fail("build/smoke", "player smoke FAILED")
        if hit == FAILED_MARKER:
            return fail("build/smoke", "player smoke FAILED: BootstrapError (内置内容/配置装载失败，见会话日志)")
        info("build/smoke", f"OK: defined-state marker reached: '{hit}'")
        info("build/smoke", "player smoke PASSED")
        return EXIT_OK
    finally:
        if installed and not args.keep_installed:
            channel.uninstall()
            info("build/smoke", "adb uninstall done (cleanup)")


# ---- 桌面通道（保持原有行为） ----


def project_log_path(project):
    settings_path = project / "ProjectSettings" / "ProjectSettings.asset"
    company, product = "DefaultCompany", "Test"
    if settings_path.is_file():
        text = settings_path.read_text(encoding="utf-8", errors="replace")
        match = re.search(r"companyName:\s*(\S+)", text)
        if match:
            company = match.group(1)
        match = re.search(r"productName:\s*(\S+)", text)
        if match:
            product = match.group(1)
    return Path.home() / "AppData" / "LocalLow" / company / product / "Player.log"


def assert_mem_loop(log_text, cycles, max_growth):
    pattern = re.compile(
        r"\[MemLoop\] cycle=(\d+) allocMB=([\d.]+) monoMB=([\d.]+) active=(\d+) pooled=(\d+) held=(\d+)"
    )
    rows = []
    for line in log_text.splitlines():
        match = pattern.search(line)
        if match:
            rows.append((int(match.group(1)), float(match.group(2)), int(match.group(4)), int(match.group(5)), int(match.group(6))))
    if len(rows) != cycles:
        info("build/smoke", f"FAIL: expected {cycles} cycle lines, found {len(rows)}")
        return False
    for row in rows:
        if row[2] != 0 or row[3] != 1 or row[4] != 1:
            info("build/smoke", f"FAIL: residency drifted at cycle {row[0]}: active={row[2]} pooled={row[3]} held={row[4]}")
            return False
    first = sum(row[1] for row in rows[:5]) / 5
    last = sum(row[1] for row in rows[-5:]) / 5
    growth = round(last - first, 2)
    info("build/smoke", f"mem trend: first5avg={first:.1f}MB last5avg={last:.1f}MB growth={growth}MB (budget {max_growth}MB)")
    if growth > max_growth:
        info("build/smoke", "FAIL: memory growth over budget")
        return False
    return True


def assert_input_modal(log_text):
    required = [
        r"\[InputModal\] baseline ok frames=\d+ blocked=0 move=1\.00 fire=1",
        r"\[InputModal\] modal-open form=\d+ isOpen=True topModal=\d+ blockers=\d+",
        r"\[InputModal\] modal ok frames=\d+ by=ui\.modal zero=1 disposed=\+\d+",
        r"\[InputModal\] modal-close form=\d+ isOpen=False modal=False",
        r"\[InputModal\] recovered ok frames=\d+ blocked=0 move=1\.00 fire=1 firstFrameZero=0",
    ]
    for pattern in required:
        if re.search(pattern, log_text) is None:
            info("build/smoke", f"FAIL: missing/incorrect phase line: {pattern}")
            return False
    if "[InputModal] fail" in log_text:
        info("build/smoke", "FAIL: probe reported failure")
        return False
    return True


def run_desktop_smoke(args, project):
    exe = Path(args.exe_path)
    if not exe.is_absolute():
        exe = project / exe
    if not exe.is_file():
        return fail("build/smoke", f"player exe missing: {exe} (run scripts/build/build-player.py first)")

    player_log = project_log_path(project)
    info("build/smoke", f"Player smoke | exe: {exe}")
    info("build/smoke", f"Player.log: {player_log}")

    if sys.platform == "win32":
        subprocess.run(["taskkill", "/F", "/IM", exe.name], capture_output=True, text=True)
    time.sleep(1)
    if player_log.is_file():
        player_log.unlink(missing_ok=True)

    process = subprocess.Popen([str(exe), *args.player_arg], cwd=str(exe.parent))
    markers = MARKERS
    timeout = args.timeout_sec
    if args.mem_loop:
        if timeout == 30:
            timeout = 240
        markers = ["[MemLoop] fail", "[MemLoop] done"]
    if args.input_modal:
        if timeout == 30:
            timeout = 240
        markers = ["[InputModal] fail", "[InputModal] done"]
    mode = "memloop" if args.mem_loop else "startup"
    info("build/smoke", f"launched (pid {process.pid}); mode={mode}; polling up to {timeout}s")

    deadline = time.monotonic() + timeout
    hit = None
    last_log = ""
    while time.monotonic() < deadline:
        time.sleep(2)
        if process.poll() is not None:
            if player_log.is_file():
                last_log = player_log.read_text(encoding="utf-8", errors="replace")
                hit = next((marker for marker in markers if marker in last_log), None)
            if hit is None:
                info("build/smoke", f"process exited early (code {process.returncode})")
            break
        if player_log.is_file():
            last_log = player_log.read_text(encoding="utf-8", errors="replace")
            hit = next((marker for marker in markers if marker in last_log), None)
            if hit:
                break

    alive = process.poll() is None
    if alive:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()

    if hit:
        if args.mem_loop:
            if hit == "[MemLoop] fail" or not assert_mem_loop(last_log, args.mem_loop_cycles, args.mem_loop_max_growth_mb):
                return fail("build/smoke", "memloop smoke FAILED")
        if args.input_modal:
            if hit == "[InputModal] fail" or not assert_input_modal(last_log):
                return fail("build/smoke", "inputmodal smoke FAILED")
        info("build/smoke", f"OK: defined-state marker reached: '{hit}' (process alive: {alive})")
        info("build/smoke", "memloop smoke PASSED" if args.mem_loop else "inputmodal smoke PASSED" if args.input_modal else "player smoke PASSED")
        return EXIT_OK

    info("build/smoke", "FAIL: no defined-state marker within timeout - last log lines:")
    for line in last_log.splitlines()[-40:]:
        print(f"    {line}")
    return fail("build/smoke", "player smoke FAILED")


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
    try:
        if args.platform == "android":
            log_path = session_log_artifact(project, args.log_artifact)
            return run_android_smoke(args, project, log_path)
        return run_desktop_smoke(args, project)
    except RuntimeError as exc:
        return fail("build/smoke", str(exc))


if __name__ == "__main__":
    sys.exit(main())
