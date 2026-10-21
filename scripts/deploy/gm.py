# -*- coding: utf-8 -*-
"""Cloud GM endpoint caller: SSH tunnel + POST one action."""

import argparse
import json
import os
import re
import socket
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402
from _gm_contract import action_spec, actions, load as load_contract  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _ssh_host import resolve_ssh_host  # noqa: E402

def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Call the cloud GM endpoint")
    parser.add_argument("--action", default="")
    parser.add_argument("--room", default="")
    parser.add_argument("--player", type=int, default=-1)
    parser.add_argument("--flags", type=int, default=-1)
    parser.add_argument("--file", default="")
    parser.add_argument("--no-death", action="store_true")
    parser.add_argument("--infinite-ammo", action="store_true")
    parser.add_argument("--ssh-host", default="")
    parser.add_argument("--remote-host", default="10.8.0.1")
    parser.add_argument("--remote-port", type=int, default=9101)
    parser.add_argument("--local-port", type=int, default=19101)
    parser.add_argument("--keep-tunnel", action="store_true")
    return parser.parse_args(argv)


def resolve_key(project):
    value = os.environ.get("LITEGAME_GM_KEY")
    if value and value.strip():
        return value.strip()
    path = project / "LocalNotes" / "cloud-host.md"
    if not path.is_file():
        raise RuntimeError(f"找不到密钥来源：环境变量 LITEGAME_GM_KEY 未设，且 {path} 不存在")
    in_block = False
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.strip() == "```":
            if in_block:
                break
            in_block = True
            continue
        if in_block and re.match(r"^[A-Za-z0-9+/]{16,}={0,2}$", line.strip()):
            return line.strip()
    raise RuntimeError(f"解析不到密钥：{path} 中没有 base64 密钥块（或请设置用户环境变量 LITEGAME_GM_KEY）")


def wait_port(port, timeout=10.0):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.25):
                return True
        except OSError:
            time.sleep(0.25)
    return False


def build_request(contract, action, *, room="", player=-1, flags=-1, file_name="",
                  no_death=False, infinite_ammo=False):
    """Validate CLI inputs against the contract; return (path, body dict)."""
    spec = action_spec(contract, action)
    if action == "rules" and flags < 0:
        flags = (1 if no_death else 0) | (2 if infinite_ammo else 0)
    provided = {"room": room, "player": player, "flags": flags, "file": file_name}
    body = {}
    missing = []
    for field in spec.get("request", []):
        name = field["name"]
        value = provided.get(name, "")
        absent = value is None or value == "" or (isinstance(value, int) and value < 0)
        if absent:
            if field.get("required"):
                missing.append("--" + name)
            continue
        body[name] = value
    if missing:
        raise ValueError(f"action {action} needs {', '.join(missing)}")
    return spec["path"], body


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    project = repo_root()
    try:
        contract = load_contract(project)
    except (OSError, ValueError) as exc:
        return fail("deploy/gm", f"GM contract unreadable: {exc}")

    known = actions(contract)
    if not args.action:
        names = "|".join(sorted(known))
        print(f"[deploy/gm] usage: gm.py --action <{names}> [--room <id>] [--player <n>] "
              "[--flags <n>] [--file <name>] [--no-death] [--infinite-ammo]")
        return 2
    if args.action not in known:
        print(f"[deploy/gm] unknown action: {args.action}（known: {', '.join(sorted(known))}）")
        return 2
    try:
        action_path, payload = build_request(
            contract, args.action, room=args.room, player=args.player, flags=args.flags,
            file_name=args.file, no_death=args.no_death, infinite_ammo=args.infinite_ammo)
    except ValueError as exc:
        print(f"[deploy/gm] {exc}")
        return 2
    body = json.dumps(payload, separators=(",", ":")).encode("utf-8")

    try:
        key = resolve_key(project)
        ssh_host = args.ssh_host.strip() or resolve_ssh_host()
        forward = f"{args.local_port}:{args.remote_host}:{args.remote_port}"
        process = subprocess.Popen(
            ["ssh", "-N", "-L", forward, ssh_host],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        try:
            if not wait_port(args.local_port):
                raise RuntimeError(f"隧道未建立（本机 {args.local_port} 未监听）——查 ssh 别名 {ssh_host} 是否可用")
            uri = f"http://127.0.0.1:{args.local_port}{action_path}"
            info("deploy/gm", f"POST {uri} body={body.decode()}")
            request = urllib.request.Request(
                uri,
                data=body,
                method="POST",
                headers={"X-GM-Key": key, "Content-Type": "application/json"},
            )
            with urllib.request.urlopen(request, timeout=30) as response:
                print(response.read().decode("utf-8", errors="replace"))
        finally:
            if not args.keep_tunnel:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                info("deploy/gm", "隧道已关闭")
            else:
                info("deploy/gm", f"隧道保留（ssh pid={process.pid}，本机端口 {args.local_port}）")
        return EXIT_OK
    except Exception as exc:
        return fail("deploy/gm", str(exc))


if __name__ == "__main__":
    sys.exit(main())
