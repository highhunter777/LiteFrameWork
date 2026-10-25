# -*- coding: utf-8 -*-
"""Player startup smoke: launch the built exe and wait for a defined-state marker."""

import argparse
import re
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Player startup smoke")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--exe-path", default="Builds/StandaloneWindows64/Test.exe")
    parser.add_argument("--timeout-sec", type=int, default=30)
    parser.add_argument("--player-arg", action="append", default=[])
    parser.add_argument("--mem-loop", action="store_true")
    parser.add_argument("--mem-loop-cycles", type=int, default=30)
    parser.add_argument("--mem-loop-max-growth-mb", type=float, default=8.0)
    parser.add_argument("--input-modal", action="store_true")
    return parser.parse_args(argv)


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


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = Path(args.project_path).resolve() if args.project_path else repo_root()
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
    markers = ["BootstrapError", "[Asset] ready"]
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


if __name__ == "__main__":
    sys.exit(main())
