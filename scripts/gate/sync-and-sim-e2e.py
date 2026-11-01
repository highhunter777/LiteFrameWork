#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""两端联测门禁（sync-and-sim）：共享哈希 → 服务端发布与启动对账 → 客户端出包 → 启动冒烟。

设计口径：
* **只复用既有入口**（`gen-build-hash.py` / `build-player.py` / `player-smoke.py`），不复制构建逻辑；
* 服务端对账用**真实启动横幅**里的 `buildHash=`（不是读同一份常量自证）——这是两端各自编译后的一致性证据；
* `ConfigHash` 不单独对账：开局 `StartGame.ConfigHash` 不等会直接拒局，冒烟能进局即等价证据；
* 每步的完整输出落 `TestResults/sync-and-sim-e2e/<时间戳>/`，摘要写同目录 `summary.json`。

用法：
    python scripts/gate/sync-and-sim-e2e.py                 # 全链（出包 + 冒烟）
    python scripts/gate/sync-and-sim-e2e.py --skip-build --skip-smoke   # 只做哈希对账（秒级）
"""

import argparse
import json
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from _common import repo_root  # noqa: E402

ROOT = repo_root()
CLIENT_HASH_FILE = ROOT / "Assets" / "LiteNet" / "Core" / "Protocol" / "BuildHash.g.cs"
DEFAULT_SERVER_CONFIG = ROOT / "deploy" / "roomserver.windows.json"
DEFAULT_PROFILE = "BuildProfiles/windows-x64.json"
DEFAULT_EXE = "Builds/StandaloneWindows64/Test.exe"
SERVER_PUBLISH_DIR = "Builds/roomserver-e2e"
BANNER_HASH_RE = re.compile(r"buildHash=([0-9a-f]{16})")
CLIENT_HASH_RE = re.compile(r'Value\s*=\s*"([0-9a-f]{16})"')


def _run(cmd, log_path, timeout):
    """执行子命令：stdout/stderr 合并落日志，返回 (ok, 输出文本)。"""
    log_path.parent.mkdir(parents=True, exist_ok=True)
    started = time.time()
    try:
        completed = subprocess.run(
            cmd,
            cwd=str(ROOT),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=timeout,
            check=False,
        )
        text = completed.stdout.decode("utf-8", errors="replace")
        log_path.write_text(text, encoding="utf-8")
        return completed.returncode == 0, text
    except subprocess.TimeoutExpired as exc:
        text = (exc.stdout or b"").decode("utf-8", errors="replace")
        log_path.write_text(text + f"\n[timeout after {timeout}s]", encoding="utf-8")
        return False, text
    except FileNotFoundError as exc:
        log_path.write_text(str(exc), encoding="utf-8")
        return False, str(exc)
    finally:
        _ = time.time() - started


def read_client_hash():
    text = CLIENT_HASH_FILE.read_text(encoding="utf-8")
    match = CLIENT_HASH_RE.search(text)
    return match.group(1) if match else None


def strip_cloud_sections(source, target):
    """去掉 lobby/settlement 分区：本地冒烟不该向云侧注册或提交。"""
    data = json.loads(source.read_text(encoding="utf-8"))
    data.pop("lobby", None)
    data.pop("settlement", None)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return target


def step_check_shared(logs):
    ok, text = _run(
        [sys.executable, "scripts/codegen/toolbox.py", "build-hash", "--check-shared"],
        logs / "check-shared.log",
        300,
    )
    return {"step": "check-shared", "ok": ok, "tail": text.strip().splitlines()[-3:]}


def step_server_reconcile(logs, config_path, duration_ms):
    publish_ok, publish_text = _run(
        ["dotnet", "publish", "Server/RoomServer/RoomServer.csproj", "-c", "Release", "-o", SERVER_PUBLISH_DIR],
        logs / "server-publish.log",
        900,
    )
    if not publish_ok:
        return {"step": "server-reconcile", "ok": False, "reason": "publish failed",
                "tail": publish_text.strip().splitlines()[-5:]}

    local_config = strip_cloud_sections(config_path, ROOT / "Temp" / "roomserver-e2e.json")
    dll = Path(SERVER_PUBLISH_DIR) / "RoomServer.dll"
    if not dll.is_file():
        return {"step": "server-reconcile", "ok": False, "reason": f"missing {dll}"}

    ok, text = _run(
        ["dotnet", str(dll), "--config", str(local_config), "--allow-insecure-local",
         "--port", "0", "--duration", str(duration_ms)],
        logs / "server-boot.log",
        300,
    )
    match = BANNER_HASH_RE.search(text)
    server_hash = match.group(1) if match else None
    return {
        "step": "server-reconcile",
        "ok": ok and server_hash is not None,
        "serverHash": server_hash,
        "reason": None if server_hash else "启动横幅未输出 buildHash（启动失败？见 server-boot.log）",
    }


def stop_editor_play(logs):
    """进入出包前先把 Editor 从 Play 模式拉回：PlayMode 测试可能留下 Play，
    而 build-player 的 bundle 构建在 Play 中只会返回 "starting" 并重试。"""
    unity = shutil.which("unity")
    if not unity:
        (logs / "editor-stop.log").write_text("skip: unity CLI not on PATH\n", encoding="utf-8")
        return
    try:
        completed = subprocess.run(
            [unity, "command", "editor_stop", "--project-path", str(ROOT)],
            cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=60,
        )
        (logs / "editor-stop.log").write_text(
            (completed.stdout or "") + (completed.stderr or ""),
            encoding="utf-8",
        )
    except (OSError, subprocess.SubprocessError) as exc:
        (logs / "editor-stop.log").write_text(f"skip: {exc}\n", encoding="utf-8")


def step_build_player(logs, profile):
    stop_editor_play(logs)
    ok, text = _run(
        [sys.executable, "scripts/build/build-player.py", "--profile", profile],
        logs / "build-player.log",
        3600,
    )
    return {"step": "build-player", "ok": ok, "tail": text.strip().splitlines()[-5:]}


def step_smoke(logs, exe_path):
    ok, text = _run(
        [sys.executable, "scripts/build/player-smoke.py", "--platform", "desktop", "--exe-path", exe_path],
        logs / "player-smoke.log",
        600,
    )
    return {"step": "player-smoke", "ok": ok, "tail": text.strip().splitlines()[-5:]}


def main(argv=None):
    parser = argparse.ArgumentParser(description="两端联测门禁（sync-and-sim）")
    parser.add_argument("--profile", default=DEFAULT_PROFILE)
    parser.add_argument("--exe-path", default=DEFAULT_EXE)
    parser.add_argument("--server-config", default=str(DEFAULT_SERVER_CONFIG))
    parser.add_argument("--duration-ms", type=int, default=4000, help="服务端跑满自退时长")
    parser.add_argument("--skip-build", action="store_true", help="跳过客户端出包（只做哈希对账）")
    parser.add_argument("--skip-smoke", action="store_true", help="跳过启动冒烟")
    parser.add_argument("--results-dir", default="TestResults/sync-and-sim-e2e")
    args = parser.parse_args(argv)

    stamp = time.strftime("%Y%m%d-%H%M%S")
    logs = ROOT / args.results_dir / stamp
    logs.mkdir(parents=True, exist_ok=True)

    steps = []
    client_hash = read_client_hash()
    if client_hash is None:
        steps.append({"step": "read-client-hash", "ok": False,
                      "reason": f"未在 {CLIENT_HASH_FILE.relative_to(ROOT)} 找到 BuildHash.Value"})
    else:
        steps.append({"step": "read-client-hash", "ok": True, "clientHash": client_hash})

    if all(step["ok"] for step in steps):
        steps.append(step_check_shared(logs))

    if all(step["ok"] for step in steps):
        server = step_server_reconcile(logs, Path(args.server_config), args.duration_ms)
        steps.append(server)
        if server.get("ok") and server.get("serverHash") != client_hash:
            server["ok"] = False
            server["reason"] = f"两端 BuildHash 不一致：client={client_hash} server={server.get('serverHash')}"

    if all(step["ok"] for step in steps) and not args.skip_build:
        steps.append(step_build_player(logs, args.profile))

    if all(step["ok"] for step in steps) and not args.skip_smoke:
        steps.append(step_smoke(logs, args.exe_path))

    ok = all(step["ok"] for step in steps)
    summary = {
        "ok": ok,
        "at": stamp,
        "clientHash": client_hash,
        "steps": steps,
        "logDir": str(logs.relative_to(ROOT)),
        "note": "ConfigHash 不单独对账：开局不等会拒局，冒烟进局即等价证据",
    }
    (logs / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    for step in steps:
        mark = "OK  " if step["ok"] else "FAIL"
        detail = step.get("reason") or step.get("serverHash") or ""
        print(f"[e2e] {mark} {step['step']} {detail}".rstrip())
    print(f"[e2e] 证据：{summary['logDir']}/summary.json")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
