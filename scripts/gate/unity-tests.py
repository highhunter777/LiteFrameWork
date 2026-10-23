# -*- coding: utf-8 -*-
"""Run Unity EditMode/PlayMode tests through unity-pipeline and fail on test failures.

`unity command run_tests` exits 0 even when tests fail (the failure is inside its JSON
summary), so gates must parse the summary instead of trusting the process exit code.
"""

import argparse
import json
import os
import subprocess
import sys
import time


def find_summary(text):
    decoder = json.JSONDecoder()
    for index, char in enumerate(text):
        if char != "{":
            continue
        try:
            payload, _ = decoder.raw_decode(text[index:])
        except json.JSONDecodeError:
            continue
        if isinstance(payload, dict) and "Summary" in payload:
            return payload
    return None


def _run_json(args, timeout=120):
    """跑一条 unity 命令并取 JSON 结果；失败返回 None（由调用方决定是否阻断）。"""
    try:
        result = subprocess.run(
            ["unity", "--format", "json"] + args,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return None
    try:
        return json.loads(result.stdout or "{}")
    except json.JSONDecodeError:
        return None


def _result(payload):
    return ((payload or {}).get("data") or {}).get("result") or {}


def _newest_seconds(path, suffix, recursive):
    newest = 0.0
    if not os.path.exists(path):
        return newest
    if recursive:
        for dirpath, _dirnames, filenames in os.walk(path):
            for name in filenames:
                if name.endswith(suffix):
                    try:
                        newest = max(newest, os.path.getmtime(os.path.join(dirpath, name)))
                    except OSError:
                        pass
        return newest
    for name in os.listdir(path):
        if name.endswith(suffix):
            try:
                newest = max(newest, os.path.getmtime(os.path.join(path, name)))
            except OSError:
                pass
    return newest


def _compile_failed(message="unity compilation failed — tests would run a stale assembly; fix compile errors first"):
    print(message, file=sys.stderr)
    return False


def ensure_compiled(wait_seconds=180):
    """跑测前保证程序集是当前源码编译出来的。

    两个真空（实测都踩过）：
    1. 源码比程序集新时 `run_tests` 仍会拿旧程序集执行——写文件与 Unity 资产导入之间有竞态，
       新用例还没进程序集就“全绿”；这里按 mtime 判定并强制重编译。
    2. 编译失败时 `run_tests` 同样拿上一次成功的旧程序集执行（Editor 程序集 CS0118 实测）；
       这里查 `console_status.groundTruth.compilationFailed` 直接判红。
    """
    assemblies = os.path.join("Library", "ScriptAssemblies")
    if _newest_seconds("Assets", ".cs", True) > _newest_seconds(assemblies, ".dll", False):
        _run_json(["command", "--timeout", "120", "recompile"])
        deadline = time.time() + wait_seconds
        while True:
            status = _result(_run_json(["command", "--timeout", "60", "recompile_status"], timeout=90))
            if status.get("failed") or status.get("compilationFailed"):
                return _compile_failed()
            if status.get("status") in ("completed", "up_to_date") and not status.get("compiling"):
                break
            if time.time() >= deadline:
                print(f"unity still compiling after {wait_seconds}s", file=sys.stderr)
                return False
            time.sleep(2)

    ground = _result(_run_json(["command", "--timeout", "60", "console_status"], timeout=90)).get("groundTruth") or {}
    if ground.get("compilationFailed"):
        return _compile_failed()
    return True


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, OSError):
            pass

    parser = argparse.ArgumentParser(description="Unity test gate")
    parser.add_argument("--mode", default="editor", choices=["editor", "playmode", "all"])
    parser.add_argument("--filter", required=True, help="test name / assembly / category filter")
    parser.add_argument("--filter-type", default=None, choices=["testName", "assembly", "category"])
    parser.add_argument("--timeout", type=int, default=600)
    args = parser.parse_args(argv)

    if not ensure_compiled():
        return 1

    # 命令级超时必须交给 unity CLI（`command --timeout`）：CLI 默认 30s，慢套件（编译+夹具）
    # 会被误判超时；python 侧只做兜底，留出 CLI 往返余量。
    command = ["unity", "command", "--timeout", str(args.timeout), "run_tests", "--mode", args.mode, "--filter", args.filter]
    if args.filter_type:
        command.extend(["--filter_type", args.filter_type])

    try:
        result = subprocess.run(
            command,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=args.timeout + 120,
        )
    except FileNotFoundError:
        print("unity CLI not found on PATH", file=sys.stderr)
        return 1
    except subprocess.TimeoutExpired:
        print(f"unity run_tests timed out after {args.timeout}s", file=sys.stderr)
        return 1

    output = (result.stdout or "") + (result.stderr or "")
    print(output)

    if result.returncode != 0:
        print(f"unity run_tests failed (exit {result.returncode})", file=sys.stderr)
        return 1

    payload = find_summary(output)
    if payload is None:
        print("unity run_tests produced no JSON summary", file=sys.stderr)
        return 1

    summary = payload.get("Summary") or {}
    total = int(summary.get("Total", 0) or 0)
    failed = int(summary.get("Failed", 0) or 0)
    if total <= 0:
        print(f"unity run_tests selected 0 tests (filter: {args.filter})", file=sys.stderr)
        return 1
    if failed > 0:
        print(f"unity run_tests: {failed}/{total} failed", file=sys.stderr)
        return 1

    print(f"unity run_tests: {total}/{total} passed", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
