# -*- coding: utf-8 -*-
"""Shared SSH destination resolver (tunnel-first)."""

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
    probe_timeout_ms=800,
    tunnel_alias="litegame-cloud-tunnel",
    public_alias="litegame-cloud",
):
    explicit = os.environ.get("LITEGAME_SSH_HOST") or _user_env("LITEGAME_SSH_HOST")
    if explicit and explicit.strip():
        print(f"[ssh] 目的地（LITEGAME_SSH_HOST 显式指定）：{explicit.strip()}")
        return explicit.strip()

    if not _reachable(tunnel_address, tunnel_port, probe_timeout_ms):
        print(
            f"[ssh] 隧道 {tunnel_address}:{tunnel_port} 不可达 → 回退公网别名 {public_alias}"
            "（隧道未起时先看服务 WireGuardTunnel；公网 22 可能被安全组挡）"
        )
        return public_alias

    try:
        config = subprocess.run(
            ["ssh", "-G", tunnel_alias], capture_output=True, text=True, encoding="utf-8", errors="replace"
        ).stdout
    except OSError:
        config = ""
    if re.search(rf"(?mi)^hostname\s+{re.escape(tunnel_address)}\s*$", config) is None:
        print(f"[ssh] 隧道可达，但别名 {tunnel_alias} 未配置为 HostName {tunnel_address}——本次回退 {public_alias}")
        return public_alias
    print(f"[ssh] 隧道 {tunnel_address}:{tunnel_port} 可达 → 用别名 {tunnel_alias}")
    return tunnel_alias
