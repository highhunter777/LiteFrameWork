# -*- coding: utf-8 -*-
"""Windows cloud-host operations, uploaded as C:/litegame/remote_runner.py.

Internal payload: not executed on the developer machine. Keep this file
stdlib-only so the cloud host needs no pip packages.
"""

import argparse
import os
import re
import subprocess
import sys
import tarfile
import time
from pathlib import Path

CLOUD_ROOT = Path(os.environ.get("LITEGAME_CLOUD_ROOT", r"C:\litegame"))
ROOM_TASK = os.environ.get("LITEGAME_ROOM_TASK", "LiteGame-Room")
ROOM_EXE = "RoomServer.exe"
ROOM_TGZ = CLOUD_ROOT / "room.tgz"
TABLES_TGZ = CLOUD_ROOT / "GameData-Config.tgz"
ROOM_LOG = CLOUD_ROOT / "room" / "out.log"
WG_HOME = Path(os.environ.get("LITEGAME_WG_HOME", str(CLOUD_ROOT / "wg")))
WG_EXE = Path(os.environ.get("LITEGAME_WG_EXE", r"C:\Program Files\WireGuard\wg.exe"))
WIREGUARD_EXE = Path(
    os.environ.get("LITEGAME_WIREGUARD_EXE", r"C:\Program Files\WireGuard\wireguard.exe")
)
WG_CONF = WG_HOME / "wg0.conf"
SERVER_PUB = WG_HOME / "server.pub"


def emit(message):
    print(message, flush=True)


def run(command, **kwargs):
    # Windows console tools (tasklist/schtasks) emit the ANSI codepage, not UTF-8.
    kwargs.setdefault("encoding", "mbcs" if os.name == "nt" else "utf-8")
    kwargs.setdefault("errors", "replace")
    return subprocess.run(command, capture_output=True, text=True, **kwargs)


def process_running(name):
    result = run(["tasklist", "/FI", f"IMAGENAME eq {name}", "/NH"])
    return name.lower() in (result.stdout or "").lower()


def stop_room():
    run(["schtasks", "/End", "/TN", ROOM_TASK])
    run(["taskkill", "/F", "/IM", ROOM_EXE])
    time.sleep(3)
    emit(f"stopped={str(not process_running(ROOM_EXE)).lower()}")
    return 0


def extract_tgz(archive, destination):
    archive = Path(archive)
    if not archive.is_file():
        emit(f"missing={archive}")
        return 1
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=True)
    try:
        with tarfile.open(archive, "r:gz") as handle:
            try:
                handle.extractall(destination, filter="data")
            except TypeError:
                handle.extractall(destination)
    except (OSError, tarfile.TarError) as exc:
        emit(f"extract_error={exc}")
        return 1
    return 0


def query_task_state():
    query = run(["schtasks", "/Query", "/TN", ROOM_TASK, "/FO", "LIST"])
    for line in (query.stdout or "").splitlines():
        if re.match(r"^\s*(Status|状态|模式)\s*:", line, re.I):
            return line.split(":", 1)[1].strip()
    return "unknown"


def emit_log_markers():
    if not ROOM_LOG.is_file():
        return
    lines = ROOM_LOG.read_text(encoding="utf-8", errors="replace").splitlines()
    for pattern in (r"buildHash=", r"0x[0-9a-fA-F]{8}"):
        matches = [line for line in lines if re.search(pattern, line)]
        if matches:
            emit(matches[-1])


def start_room(skip_tables):
    extract_code = extract_tgz(ROOM_TGZ, CLOUD_ROOT)
    emit(f"extract={extract_code}")
    if skip_tables:
        TABLES_TGZ.unlink(missing_ok=True)
        emit("tables=skipped")
    elif TABLES_TGZ.is_file():
        tables_code = extract_tgz(TABLES_TGZ, CLOUD_ROOT / "GameData" / "Config")
        emit(f"tables={tables_code}")
    else:
        emit("tables=missing")

    start_result = run(["schtasks", "/Run", "/TN", ROOM_TASK])
    emit(f"start={start_result.returncode}")
    time.sleep(8)
    emit(f"process={'RUNNING' if process_running(ROOM_EXE) else 'NOT RUNNING'}")
    emit(f"task={query_task_state()}")
    emit_log_markers()
    return 0 if extract_code == 0 else 1


def status_room():
    emit(f"process={'RUNNING' if process_running(ROOM_EXE) else 'NOT RUNNING'}")
    emit(f"task={query_task_state()}")
    emit_log_markers()
    return 0


def next_ip(conf_text):
    used = [int(value) for value in re.findall(r"AllowedIPs\s*=\s*10\.8\.0\.(\d+)/32", conf_text)]
    return f"10.8.0.{max(used) + 1 if used else 2}"


def wg_peer(name, ip, endpoint, dry_run):
    if re.fullmatch(r"[A-Za-z0-9_-]{1,32}", name) is None:
        emit("INVALID_NAME")
        return 2
    if not WG_EXE.is_file():
        emit("NO_WG")
        return 3
    if not WG_CONF.is_file():
        emit("NO_CONF")
        return 4
    conf_text = WG_CONF.read_text(encoding="utf-8", errors="replace")
    if re.search(rf"^# {re.escape(name)}$", conf_text, re.M):
        emit("DUPLICATE")
        return 2
    if not ip:
        ip = next_ip(conf_text)

    private_result = run([str(WG_EXE), "genkey"])
    private_key = (private_result.stdout or "").strip()
    if private_result.returncode != 0 or not private_key:
        emit("WG_ERROR=genkey")
        return 1
    public_result = subprocess.run(
        [str(WG_EXE), "pubkey"],
        input=private_key + "\n",
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    public_key = (public_result.stdout or "").strip()
    if public_result.returncode != 0 or not public_key:
        emit("WG_ERROR=pubkey")
        return 1

    if not SERVER_PUB.is_file():
        emit("NO_SERVER_PUB")
        return 1
    server_public = SERVER_PUB.read_text(encoding="utf-8", errors="replace").strip()

    if not dry_run:
        block = (
            f"\r\n[Peer]\r\n# {name}\r\nPublicKey = {public_key}\r\n"
            f"AllowedIPs = {ip}/32\r\n"
        )
        with open(WG_CONF, "a", encoding="ascii", newline="") as handle:
            handle.write(block)
        run([str(WIREGUARD_EXE), "/uninstalltunnelservice", "wg0"])
        time.sleep(1)
        run([str(WIREGUARD_EXE), "/installtunnelservice", str(WG_CONF)])
        time.sleep(2)
    emit("BEGIN_CONF")
    emit("[Interface]")
    emit(f"PrivateKey = {private_key}")
    emit(f"Address = {ip}/24")
    emit("MTU = 1420")
    emit("")
    emit("[Peer]")
    emit(f"PublicKey = {server_public}")
    emit(f"Endpoint = {endpoint}")
    emit("AllowedIPs = 10.8.0.0/24")
    emit("PersistentKeepalive = 25")
    emit("END_CONF")
    emit(f"ADDR {ip}")
    return 0


def build_parser():
    parser = argparse.ArgumentParser(prog="remote_runner.py", description="LiteGame cloud-host operations")
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("room-stop", help="stop the RoomServer process and scheduled task")
    start = subparsers.add_parser("room-start", help="extract room.tgz and start the scheduled task")
    start.add_argument("--skip-tables", action="store_true")
    subparsers.add_parser("room-status", help="report RoomServer process/task state and log markers")
    peer = subparsers.add_parser("wg-peer", help="create a WireGuard peer and print its client config")
    peer.add_argument("--name", required=True)
    peer.add_argument("--ip", default="")
    peer.add_argument("--endpoint", default="")
    peer.add_argument("--dry-run", action="store_true")
    return parser


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    if sys.version_info < (3, 10):
        emit(f"PYTHON_TOO_OLD={sys.version.split()[0]}")
        return 2
    args = build_parser().parse_args(argv)
    if args.command == "room-stop":
        return stop_room()
    if args.command == "room-start":
        return start_room(args.skip_tables)
    if args.command == "room-status":
        return status_room()
    return wg_peer(args.name, args.ip, args.endpoint, args.dry_run)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # noqa: BLE001 - remote failures must stay one readable line
        print(f"ERROR={exc}", file=sys.stderr)
        sys.exit(1)
