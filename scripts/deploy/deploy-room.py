# -*- coding: utf-8 -*-
"""Deploy the cloud RoomServer (publish -> tar -> stop -> upload -> start)."""

import argparse
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _ssh_host import resolve_ssh_host  # noqa: E402
from _remote import ensure_remote_ready, run_remote_checked  # noqa: E402


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Deploy the cloud RoomServer")
    parser.add_argument("--project-path", default=None)
    parser.add_argument("--skip-publish", action="store_true")
    parser.add_argument("--with-config", action="store_true")
    parser.add_argument("--skip-tables", action="store_true")
    parser.add_argument("--ssh-host", default="")
    return parser.parse_args(argv)


def run_checked(command, label):
    result = subprocess.run(command)
    if result.returncode != 0:
        raise RuntimeError(f"{label} failed (exit {result.returncode})")


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    repo = Path(args.project_path).resolve() if args.project_path else repo_root()
    server_root = repo / "Builds" / "win-server"
    room_dir = server_root / "room"
    tgz = server_root / "room.tgz"
    tables_tgz = server_root / "GameData-Config.tgz"
    if args.skip_publish and not room_dir.is_dir():
        return fail("deploy/room", f"找不到 {room_dir}：先跑一次不带 --skip-publish 的部署（会发布产物）")

    try:
        ssh_host = args.ssh_host.strip() or resolve_ssh_host()
        if not args.skip_publish:
            info("deploy/room", "dotnet publish（win-x64 自包含）...")
            run_checked(
                [
                    "dotnet", "publish", str(repo / "Server/RoomServer/RoomServer.csproj"),
                    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                    "-o", str(room_dir), "--nologo", "-v", "q",
                ],
                "dotnet publish",
            )

        info("deploy/room", "打包 room.tgz ...")
        server_root.mkdir(parents=True, exist_ok=True)
        tar_exe = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32" / "tar.exe"
        tar = str(tar_exe) if tar_exe.is_file() else "tar"
        run_checked([tar, "-czf", str(tgz), "-C", str(server_root), "room"], "tar room")
        if not args.skip_tables:
            info("deploy/room", "打包玩法表 GameData/Config ...")
            run_checked([tar, "-czf", str(tables_tgz), "-C", str(repo / "Assets/GameData/Config"), "."], "tar tables")

        ensure_remote_ready(ssh_host)

        info("deploy/room", "停服 ...")
        run_remote_checked(ssh_host, "room-stop", label="stop service")

        info("deploy/room", "上传产物 ...")
        run_checked(["scp", "-q", str(tgz), f"{ssh_host}:C:/litegame/room.tgz"], "scp room")
        if args.with_config:
            info("deploy/room", "上传房间配置 ...")
            run_checked(
                ["scp", "-q", str(repo / "deploy/roomserver.windows.json"), f"{ssh_host}:C:/litegame/room/roomserver.json"],
                "scp config",
            )
        if not args.skip_tables:
            info("deploy/room", "上传玩法表 ...")
            run_checked(["scp", "-q", str(tables_tgz), f"{ssh_host}:C:/litegame/GameData-Config.tgz"], "scp tables")

        info("deploy/room", "解压并起服 ...")
        start_args = ["room-start"]
        if args.skip_tables:
            start_args.append("--skip-tables")
        run_remote_checked(ssh_host, *start_args, label="start service")
        info("deploy/room", "完成：判据 = task=Running + buildHash 行（应与客户端包内常量同值）")
        return EXIT_OK
    except RuntimeError as exc:
        return fail("deploy/room", str(exc))


if __name__ == "__main__":
    sys.exit(main())
