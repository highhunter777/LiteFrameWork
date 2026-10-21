# -*- coding: utf-8 -*-
"""Shared SSH destination resolver (tunnel-only; public requires explicit override)."""

import os
import re
import socket
import subprocess


def _user_env(name):
    try:
        import winreg

        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
            value, _ = winreg.QueryValueEx(key, name)
            return value
    except Exception:
        return None


def _reachable(host, port, timeout_ms):
    try:
        with socket.create_connection((host, port), timeout=timeout_ms / 1000.0):
            return True
    except OSError:
        return False


def resolve_ssh_host(
    tunnel_address="10.8.0.1",
    tunnel_port=22,
    probe_timeout_ms=3000,
    tunnel_alias="litegame-cloud-tunnel",
    public_alias="litegame-cloud",
):
    explicit = os.environ.get("LITEGAME_SSH_HOST") or _user_env("LITEGAME_SSH_HOST")
    if explicit and explicit.strip():
        print(f"[ssh] 目的地（LITEGAME_SSH_HOST 显式指定）：{explicit.strip()}")
        return explicit.strip()

    if not _reachable(tunnel_address, tunnel_port, probe_timeout_ms):
        raise RuntimeError(
            f"WireGuard 隧道 {tunnel_address}:{tunnel_port} 不可达；公网 22 已关闭。"
            "先检查本机 WireGuardTunnel 服务与云端 WireGuard 握手；"
            f"破窗时显式设置 LITEGAME_SSH_HOST={public_alias}（需临时开放安全组 22）"
        )

    try:
        config = subprocess.run(
            ["ssh", "-G", tunnel_alias], capture_output=True, text=True, encoding="utf-8", errors="replace"
        ).stdout
    except OSError:
        config = ""
    if re.search(rf"(?mi)^hostname\s+{re.escape(tunnel_address)}\s*$", config) is None:
        raise RuntimeError(
            f"隧道可达，但 SSH 别名 {tunnel_alias} 未配置为 HostName {tunnel_address}；"
            "按 Docs/项目说明/10-Windows云主机部署.md §4.1 配置 ~/.ssh/config"
        )
    print(f"[ssh] 隧道 {tunnel_address}:{tunnel_port} 可达 → 用别名 {tunnel_alias}")
    return tunnel_alias
