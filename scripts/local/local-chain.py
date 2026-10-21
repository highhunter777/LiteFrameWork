# -*- coding: utf-8 -*-
"""Local test chain: machine-private keys + Meta/RoomServer launch/status."""

import argparse
import base64
import json
import os
import re
import socket
import subprocess
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402

DEFAULT_ROOM_ID = "lobby-default"
KEYS = ("ENVELOPE_PSK", "AUTH_SIGNING_KEY", "LOBBY_INSTANCE_KEY", "LOBBY_TICKET_KEY", "SETTLEMENT_KEY")


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Local test chain", epilog="Commands: init, meta, room, up, status, keys")
    parser.add_argument("command", nargs="?", default="")
    parser.add_argument("--force", action="store_true")
    return parser.parse_args(argv)


def new_key32():
    return base64.b64encode(os.urandom(32)).decode("ascii")


def read_env(path):
    data = {}
    if not path.is_file():
        return data
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        text = line.strip()
        if not text or text.startswith("#") or "=" not in text:
            continue
        key, value = text.split("=", 1)
        data[key.strip()] = value.strip()
    return data


def get_config(env_file):
    if not env_file.is_file():
        raise RuntimeError("deploy/.env not found - run: local-chain.py init")
    return read_env(env_file)


def write_deploy_args(path, envelope_key, meta_url):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f"-envelope.key={envelope_key}\n-meta.url={meta_url}\n", encoding="utf-8")
    info("local/chain", f"deploy.args written: {path} (editor/exe zero-arg startup)")


def start_console(title, root, inner):
    command = f'title {title} & cd /d "{root}" & {inner}'
    if sys.platform == "win32":
        subprocess.Popen(["cmd", "/k", command], creationflags=subprocess.CREATE_NEW_CONSOLE)
    else:
        subprocess.Popen(["sh", "-c", inner], cwd=str(root))


def invoke_init(root, env_file, deploy_args, force):
    if env_file.is_file() and not force:
        info("local/chain", "deploy/.env already exists (keep machine keys). Re-run with --force to regenerate.")
    else:
        lines = [
            "# machine-private local chain keys - never commit",
            f"ENVELOPE_PSK={new_key32()}",
            f"AUTH_SIGNING_KEY={new_key32()}",
            f"LOBBY_INSTANCE_KEY={new_key32()}",
            f"LOBBY_TICKET_KEY={new_key32()}",
            f"SETTLEMENT_KEY={new_key32()}",
            "# --- machine endpoints/paths (edit to taste) ---",
            "META_BIND=http://127.0.0.1:5000",
            "META_URL=http://127.0.0.1:5000",
            "MONGO_CONNECTION=mongodb://localhost:27017",
            "MONGO_DB=metadb",
            "ROOM_CONFIG=Config/roomserver.json",
            "ROOM_PORT=17777",
            f"LOBBY_DEFAULT_ROOM_ID={DEFAULT_ROOM_ID}",
        ]
        env_file.parent.mkdir(parents=True, exist_ok=True)
        env_file.write_text("\r\n".join(lines) + "\r\n", encoding="utf-8", newline="")
        info("local/chain", f"deploy/.env written: {env_file}")
    cfg = read_env(env_file)
    write_deploy_args(deploy_args, cfg.get("ENVELOPE_PSK", ""), cfg.get("META_URL", ""))
    info("local/chain", "Full chain ready-to-run: local-chain.py up (needs local MongoDB)")


def convert_meta_appsettings(cfg, out_path):
    room_id = cfg.get("LOBBY_DEFAULT_ROOM_ID") or DEFAULT_ROOM_ID
    payload = {
        "Meta": {
            "BindAddress": cfg.get("META_BIND", ""),
            "MongoConnectionString": cfg.get("MONGO_CONNECTION", ""),
            "MongoDatabaseName": cfg.get("MONGO_DB", ""),
            "AuthSigningKeyBase64": cfg.get("AUTH_SIGNING_KEY", ""),
            "LobbyInstanceKeyBase64": cfg.get("LOBBY_INSTANCE_KEY", ""),
            "LobbyTicketKeyBase64": cfg.get("LOBBY_TICKET_KEY", ""),
            "LobbyTicketKeyId": "l1",
            "LobbyTicketAudience": "lobby",
            "LobbyDefaultRoomId": room_id,
            "SettlementInstanceKeyBase64": cfg.get("SETTLEMENT_KEY", ""),
            "EnableSampleEndpoints": False,
        }
    }
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def invoke_meta(root, env_file, deploy_args):
    cfg = get_config(env_file)
    info("local/chain", "building MetaServer...")
    result = subprocess.run(["dotnet", "build", str(root / "Server/MetaServer/MetaServer.csproj"), "--nologo", "-v", "q"])
    if result.returncode != 0:
        raise RuntimeError(f"MetaServer build failed (exit {result.returncode})")
    convert_meta_appsettings(cfg, root / "Server/MetaServer/bin/Debug/net8.0/appsettings.json")
    info("local/chain", "keys applied to bin appsettings.json")
    info("local/chain", "persistence ON - make sure local MongoDB is running" if cfg.get("MONGO_CONNECTION") else "persistence OFF (Mongo empty in deploy/.env)")
    start_console("MetaServer (local)", root, f'dotnet run --no-build --project Server\\MetaServer -- --bind {cfg.get("META_BIND", "")}')
    info("local/chain", "MetaServer starting in new window")


def invoke_room(root, env_file):
    cfg = get_config(env_file)
    inner = (
        f'dotnet run --project Server\\RoomServer -- --config {cfg.get("ROOM_CONFIG", "")} '
        f'--port {cfg.get("ROOM_PORT", "")} --envelope-key {cfg.get("ENVELOPE_PSK", "")} '
        f'--ticket-key l1:{cfg.get("LOBBY_TICKET_KEY", "")} --lobby-instance-key {cfg.get("LOBBY_INSTANCE_KEY", "")} '
        f'--settlement-instance-key {cfg.get("SETTLEMENT_KEY", "")}'
    )
    start_console("RoomServer (local)", root, inner)
    info("local/chain", "RoomServer starting in new window; with ticket key configured F9 join is rejected - use login page")


def invoke_keys(cfg):
    for name in KEYS:
        value = cfg.get(name, "")
        masked = value[:6] + "..." if value else "(missing)"
        print(f"{name:<28} {masked}")
    for name in ("META_BIND", "META_URL", "MONGO_CONNECTION", "MONGO_DB", "ROOM_CONFIG", "ROOM_PORT"):
        print(f"{name:<28} {cfg.get(name, '')}")
    print(f"{'LOBBY_DEFAULT_ROOM_ID':<28} {cfg.get('LOBBY_DEFAULT_ROOM_ID') or DEFAULT_ROOM_ID + ' (default)'}")


def build_hash(root):
    path = root / "Assets/LiteNet/Protocol/BuildHash.g.cs"
    if not path.is_file():
        return "(BuildHash.g.cs missing)"
    match = re.search(r'public const string Value = "([0-9a-f]{16})"', path.read_text(encoding="utf-8", errors="replace"))
    return match.group(1) if match else "(BuildHash.g.cs parse failed)"


def tcp_reachable(host, port, timeout=0.5):
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True
    except OSError:
        return False


def tasklist(name):
    try:
        result = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {name}", "/NH"], capture_output=True, text=True, encoding="utf-8", errors="replace")
    except OSError:
        return False
    return name in (result.stdout or "")


def invoke_status(root, env_file, deploy_args):
    cfg = get_config(env_file)
    print("== shared identity ==")
    print(f"BuildHash (client+server, from BuildHash.g.cs) : {build_hash(root)}")
    print("== MetaServer ==")
    bind = cfg.get("META_BIND", "")
    live = False
    try:
        with urllib.request.urlopen(f"{bind}/live", timeout=2) as response:
            live = response.status == 200
    except Exception:
        pass
    state = f"RUNNING ({bind}/live 200)" if live else "process alive but /live not answering" if tasklist("MetaServer.exe") else "not running"
    print(f"process /live : {state}  [{bind}]")
    mongo = cfg.get("MONGO_CONNECTION", "")
    if mongo:
        host = re.sub(r"^mongodb://", "", mongo).split("/")[0].split(":")[0]
        port = 27017
        match = re.search(r":(\d+)", mongo)
        if match:
            port = int(match.group(1))
        print(f"Mongo        : {'TCP reachable' if tcp_reachable(host, port) else 'NOT reachable'}  [{host}:{port}]")
    else:
        print("Mongo        : persistence OFF (MongoConnectionString empty)")
    print("== RoomServer ==")
    room_port = int(cfg.get("ROOM_PORT", "17777"))
    try:
        netstat = subprocess.run(["netstat", "-ano", "-p", "udp"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout
    except OSError:
        netstat = ""
    bound = f":{room_port}" in netstat
    room_state = f"RUNNING (UDP {room_port} bound)" if bound else "process alive but port not bound" if tasklist("RoomServer.exe") else "not running"
    print(f"process /UDP  : {room_state}  [UDP {room_port}]")
    print(f"envelope      : {'PSK set (secure channel on)' if cfg.get('ENVELOPE_PSK') else 'PSK MISSING - server would refuse to start'}")
    print(f"ticket gate   : {'ticket keys exist -> full chain (F9 test join REJECTED)' if cfg.get('LOBBY_TICKET_KEY') else 'no ticket keys -> F9 test join allowed (prototype)'}")
    print("== client (baked) ==")
    if deploy_args.is_file():
        print("deploy.args   :")
        for line in deploy_args.read_text(encoding="utf-8", errors="replace").splitlines():
            print(f"  {line}")
    else:
        print("deploy.args   : not baked (run init)")
    print("== keys ==")
    invoke_keys(cfg)


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    root = repo_root()
    env_file = root / "deploy" / ".env"
    deploy_args = root / "Assets" / "StreamingAssets" / "deploy.args"
    try:
        if args.command == "init":
            invoke_init(root, env_file, deploy_args, args.force)
        elif args.command == "meta":
            invoke_meta(root, env_file, deploy_args)
        elif args.command == "room":
            invoke_room(root, env_file)
        elif args.command == "up":
            invoke_meta(root, env_file, deploy_args)
            invoke_room(root, env_file)
        elif args.command == "keys":
            invoke_keys(get_config(env_file))
        elif args.command == "status":
            invoke_status(root, env_file, deploy_args)
        else:
            print("[local/chain] Usage: local-chain.py <init|meta|room|up|status|keys> [--force]")
            print("  init   write deploy/.env (5 keys) + editor deploy.args")
            print("  meta   start MetaServer (keys from deploy/.env)")
            print("  room   start RoomServer (keys from deploy/.env)")
            print("  up     meta + room")
            print("  status client/server environment overview")
            print("  keys   masked key summary")
            return 2
        return EXIT_OK
    except RuntimeError as exc:
        return fail("local/chain", str(exc))


if __name__ == "__main__":
    sys.exit(main())
