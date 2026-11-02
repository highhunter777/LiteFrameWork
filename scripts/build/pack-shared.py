#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""共享包打包：把服务端与 Unity 共编的库打成 NuGet 包，落 LocalPackages/。

包的集合与顺序（下游依赖上游）：
    LiteGame.Sim              <- Packages/com.litegame.sim/LiteSim.csproj
    LiteGame.Net              <- Packages/com.litegame.net/LiteNet.csproj
    LiteGame.Sync             <- Packages/com.litegame.sync/LiteSync.csproj
    LiteGame.Room.Core        <- Assets/RoomServer/Core/RoomServer.Runtime.csproj
    LiteGame.Room.Application <- Assets/RoomServer/Modules/RoomServer.Application.Runtime.csproj

版本单源：SharedPackages.props 的 <LiteGameSharedVersion>；PackageId 写在各自 csproj（命令行 -p:PackageId
会让含 ProjectReference 的工程在 restore 阶段报 "Ambiguous project name"）。
客户端不受影响：Unity 仍按 asmdef 编同一份源码（双轨），本脚本只服务 dotnet 侧消费。

用法：
    python scripts/build/pack-shared.py            # 打包 + 校验
    python scripts/build/pack-shared.py --check    # 只校验 feed 里的包版本与源单源一致
"""
from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FEED = ROOT / "LocalPackages"
PROPS = ROOT / "SharedPackages.props"

PACKAGES = [
    ("LiteGame.Sim", "Packages/com.litegame.sim/LiteSim.csproj"),
    ("LiteGame.Net", "Packages/com.litegame.net/LiteNet.csproj"),
    ("LiteGame.Sync", "Packages/com.litegame.sync/LiteSync.csproj"),
    ("LiteGame.Room.Core", "Assets/RoomServer/Core/RoomServer.Runtime.csproj"),
    ("LiteGame.Room.Application", "Assets/RoomServer/Modules/RoomServer.Application.Runtime.csproj"),
]


def read_version() -> str:
    text = PROPS.read_text(encoding="utf-8-sig")
    m = re.search(r"<LiteGameSharedVersion>([^<]+)</LiteGameSharedVersion>", text)
    if not m:
        raise SystemExit("SharedPackages.props 缺少 <LiteGameSharedVersion>")
    return m.group(1).strip()


def dotnet() -> str:
    exe = shutil.which("dotnet")
    if not exe:
        raise SystemExit("dotnet 不在 PATH")
    return exe


def pack(version: str, configuration: str) -> None:
    FEED.mkdir(parents=True, exist_ok=True)
    exe = dotnet()
    for package_id, rel in PACKAGES:
        project = ROOT / rel
        if not project.exists():
            raise SystemExit(f"缺工程：{rel}")
        print(f"[pack] {package_id} {version} <- {rel}")
        proc = subprocess.run(
            [
                exe, "pack", str(project),
                "--configuration", configuration,
                "--nologo",
                f"-p:Version={version}",
                f"-p:PackageOutputPath={FEED}",
                "--verbosity", "minimal",
            ],
            cwd=str(ROOT),
        )
        if proc.returncode != 0:
            raise SystemExit(f"打包失败：{package_id}（exit {proc.returncode}）")
    purge_nuget_cache(version)
    print(f"[pack] 完成：{len(PACKAGES)} 个包 -> {FEED}")


def newest_source(rel: str) -> float:
    """工程目录下最新源文件时间（排除产物目录）——用于判定 nupkg 是否落后于源码。"""
    base = (ROOT / rel).parent
    newest = 0.0
    for dp, dn, fns in os.walk(base):
        parts = set(dp.replace('\\', '/').split('/'))
        if parts & {'.dotnet', 'obj', 'bin'}:
            continue
        for f in fns:
            if f.endswith(('.cs', '.csproj', '.props', '.proto')):
                newest = max(newest, os.path.getmtime(os.path.join(dp, f)))
    return newest


def purge_nuget_cache(version: str) -> None:
    """同版本号重打包后，NuGet 全局包目录里的旧解压件不会自动刷新——不清理就会用到旧 DLL。"""
    base = Path(os.environ.get("NUGET_PACKAGES") or (Path.home() / ".nuget" / "packages"))
    for package_id, _ in PACKAGES:
        target = base / package_id.lower() / version
        if target.is_dir():
            shutil.rmtree(target, ignore_errors=True)
            print(f"[pack] 清理 NuGet 缓存 {target}")


def check(version: str) -> int:
    if not FEED.exists():
        print(f"[pack] feed 不存在：{FEED}（先运行 python scripts/build/pack-shared.py）")
        return 1
    problems = []
    for package_id, rel in PACKAGES:
        hits = sorted(FEED.glob(f"{package_id}.*.nupkg"))
        if not hits:
            problems.append(f"缺包 {package_id} {version}")
            continue
        versions = {h.name[len(package_id) + 1:-6] for h in hits}
        if version not in versions:
            problems.append(f"{package_id} 现有 {sorted(versions)}，源单源是 {version}")
            continue
        nupkg = max(hits, key=lambda h: h.stat().st_mtime)
        src = newest_source(rel)
        if src > nupkg.stat().st_mtime:
            problems.append(f"{package_id} 落后于源码（{nupkg.name} 比源旧）")
    if problems:
        print("[pack] 过期：")
        for p in problems:
            print("  -", p)
        return 1
    print(f"[pack] OK：{len(PACKAGES)} 个包均为 {version}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="共享包打包 / 校验")
    parser.add_argument("--check", action="store_true", help="只校验 feed 与源单源版本一致")
    parser.add_argument("--ensure", action="store_true", help="缺包或版本过期时才重新打包（门禁/CI 用）")
    parser.add_argument("--configuration", default="Release")
    args = parser.parse_args()

    version = read_version()
    if args.check:
        return check(version)
    if args.ensure:
        if check(version) == 0:
            return 0
        print("[pack] feed 缺件或过期 → 重新打包")
    pack(version, args.configuration)
    return check(version)


if __name__ == "__main__":
    sys.exit(main())
