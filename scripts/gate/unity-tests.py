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
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import repo_root  # noqa: E402


def _run_json(args, timeout=120):
    """跑一条 unity 命令并取 JSON 结果；失败返回 None（由调用方决定是否阻断）。

    显式带 `--project-path`：本机可能同时打开多个 Unity 工程（实测 WindFrameWork 与
    Test 各占一个 Pipeline 端口），不指定会因“Multiple Unity Editor instances”直接失败。
    显式带 `--proxy-disable`：本脚本只连本机编辑器实例，绝不需要代理；系统代理开启时
    （实测 Clash 系 127.0.0.1:7890，CLI 的 bypass 匹配对 127.0.0.1 失效）会拦本地请求报 502。
    """
    command_args = list(args)
    if command_args and command_args[0] == "command" and "--project-path" not in command_args:
        command_args[1:1] = ["--project-path", str(repo_root())]
    try:
        result = subprocess.run(
            ["unity", "--proxy-disable", "--format", "json"] + command_args,
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
    result = ((payload or {}).get("data") or {}).get("result")
    if isinstance(result, str):
        # --format json 下部分命令的 result 是双重编码的 JSON 字符串（实测 test_status/recompile_status）
        try:
            result = json.loads(result)
        except json.JSONDecodeError:
            result = None
    return result if isinstance(result, dict) else {}


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
            # 瞬时非对象响应（桥忙/编译中文本态）不判死——按待编译继续轮询，超时兜底；
            # 只有拿到明确失败标志才判红（实测：recompile 路径首询偶发返回字符串，崩溃比等待更糟）
            if isinstance(status, dict) and (status.get("failed") or status.get("compilationFailed")):
                return _compile_failed()
            if isinstance(status, dict) and status.get("status") in ("completed", "up_to_date") and not status.get("compiling"):
                break
            if time.time() >= deadline:
                print(f"unity still compiling after {wait_seconds}s", file=sys.stderr)
                return False
            time.sleep(2)

    ground = None
    for attempt in range(3):
        ground_raw = _result(_run_json(["command", "--timeout", "60", "console_status"], timeout=90))
        if isinstance(ground_raw, dict) and isinstance(ground_raw.get("groundTruth"), dict):
            ground = ground_raw["groundTruth"]
            break
        # 域重载/服务忙时偶发空结果：重试；三次仍不可用则失败关闭（不拿旧程序集假绿）
        if attempt < 2:
            time.sleep(2)
    if ground is None:
        print("console_status unavailable after retries (groundTruth missing)", file=sys.stderr)
        return False
    if ground.get("compilationFailed"):
        return _compile_failed()
    return True


def wait_editor_ready(wait_seconds=180):
    """测试发射前等编辑器真正可接收命令：域重载/编译/Play 都结束。
    域重载期间发射 `run_tests` 会留下孤儿 request（runner 只 reattach 不启动），表现为 test_status 永 running。"""
    deadline = time.time() + wait_seconds
    while True:
        st = _result(_run_json(["command", "--timeout", "20", "editor_status"], timeout=30))
        if (isinstance(st, dict) and st.get("status") == "ready"
                and not st.get("compiling") and not st.get("domainReloadInProgress")
                and str(st.get("playMode")) == "stopped"):
            return True
        if time.time() >= deadline:
            print("editor not ready for tests (domain reload/compiling/play mode not stopped); "
                  "keep the Unity window restored and retry", file=sys.stderr)
            return False
        time.sleep(2)


def _status_mtime():
    try:
        return os.path.getmtime(os.path.join("Temp", "pipeline_test_status.json"))
    except OSError:
        return None


def run_async(args):
    """EditMode/PlayMode 统一走异步通道：同步 `run_tests` 受管线命令 30s 传输超时约束，
    长夹具会被中途取消（且可能只带回已完成用例的 summary——假绿风险）。
    异步发射后轮询 `test_status` 终态（completed/failed/error/cancelled/interrupted）判绿红。

    注意：`run_tests` 的工具参数 `--timeout` 若放在 `--filter` 之后，CLI 会丢 filter（实测跑全集）；
    这里只用工具默认 300s，外层 `args.timeout` 只做轮询上限。"""
    launch = ["unity", "--proxy-disable", "command", "--project-path", str(repo_root()),
              "run_tests", "--mode", args.mode,
              "--async_tests", "--filter", args.filter]
    if args.filter_type:
        launch.extend(["--filter_type", args.filter_type])
    stale_mtime = _status_mtime()
    started = time.time()
    try:
        subprocess.run(launch, capture_output=True, text=True, encoding="utf-8",
                       errors="replace", timeout=45)
    except subprocess.TimeoutExpired:
        pass      # 发射请求被域重载打断是预期形态——任务已在服务端启动
    except FileNotFoundError:
        print("unity CLI not found on PATH", file=sys.stderr)
        return 1

    deadline = time.time() + args.timeout
    payload = {}
    while True:
        payload = _result(_run_json(["command", "--timeout", "20", "test_status"], timeout=30))
        status = payload.get("status")
        if status in ("completed", "failed", "error", "interrupted", "cancelled"):
            # 发射前遗留的 completed 状态（上一轮/被杀任务）不得当本轮结果；等新文件出现
            mtime = _status_mtime()
            if status == "completed" and mtime is not None and mtime < started - 0.5:
                if time.time() >= deadline:
                    print("test_status stayed stale after launch——async run did not start", file=sys.stderr)
                    return 1
                time.sleep(3)
                continue
            break
        if status == "idle":
            print("test_status=idle——异步任务未启动（发射请求未达服务端）", file=sys.stderr)
            return 1
        if time.time() >= deadline:
            print(f"tests still running after {args.timeout}s (status={status})", file=sys.stderr)
            return 1
        time.sleep(3)

    print(json.dumps(payload, ensure_ascii=False, indent=2))
    if status in ("cancelled", "interrupted"):
        print(f"tests {status} before completion", file=sys.stderr)
        return 1
    if status == "error":
        print("tests error: " + str(payload.get("message")), file=sys.stderr)
        return 1

    summary = payload.get("summary") or {}
    total = int(summary.get("total", 0) or 0)
    failed = int(summary.get("failed", 0) or 0)
    results = payload.get("results") or []
    if total <= 0:
        print(f"test run selected 0 tests (filter: {args.filter})", file=sys.stderr)
        return 1
    if args.filter and (args.filter_type is None or args.filter_type.lower() == "testname"):
        if not any(args.filter.lower() in str(case.get("FullName") or "").lower() for case in results):
            print(f"filter not reflected in results (filter: {args.filter})——疑似跑了陈旧/全集结果",
                  file=sys.stderr)
            return 1
    if failed > 0:
        for case in results:
            if case.get("Status") == "Failed":
                print(f"FAILED: {case.get('FullName')}: {case.get('Message')}", file=sys.stderr)
        print(f"tests: {failed}/{total} failed", file=sys.stderr)
        return 1
    print(f"tests: {total}/{total} passed", file=sys.stderr)
    return 0


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

    if not wait_editor_ready():
        return 1

    return run_async(args)


if __name__ == "__main__":
    sys.exit(main())
