# -*- coding: utf-8 -*-
"""Create a WireGuard tester peer on the cloud host and write the client .conf."""

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from _common import EXIT_FAIL, EXIT_OK, configure_stdio, fail, info, repo_root  # noqa: E402
sys.path.insert(0, str(Path(__file__).resolve().parent))
from _ssh_host import resolve_ssh_host  # noqa: E402

REMOTE_SCRIPT = r"""param(
    [Parameter(Mandatory=$true)][string]$Name,
    [string]$Ip = '',
    [string]$Endpoint = '',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$wg = 'C:\Program Files\WireGuard\wg.exe'
$conf = 'C:\litegame\wg\wg0.conf'
if (-not (Test-Path $wg)) { Write-Output 'NO_WG'; exit 3 }
if (-not (Test-Path $conf)) { Write-Output 'NO_CONF'; exit 4 }
if (Select-String -Path $conf -Pattern ('^# ' + [regex]::Escape($Name) + '$') -Quiet) { Write-Output 'DUPLICATE'; exit 2 }
if ([string]::IsNullOrEmpty($Ip)) {
    $used = @()
    Select-String -Path $conf -Pattern 'AllowedIPs = 10\.8\.0\.([0-9]+)/32' -AllMatches |
        ForEach-Object { $_.Matches } | ForEach-Object { $used += [int]$_.Groups[1].Value }
    $next = 2
    if ($used.Count -gt 0) { $next = ($used | Measure-Object -Maximum).Maximum + 1 }
    $Ip = '10.8.0.' + $next
}
$priv = (& $wg genkey)
$pubc = ($priv | & $wg pubkey)
if (-not $DryRun) {
    Add-Content -Path $conf -Value ("`r`n[Peer]`r`n# $Name`r`nPublicKey = $pubc`r`nAllowedIPs = $Ip/32`r`n")
    & 'C:\Program Files\WireGuard\wireguard.exe' /uninstalltunnelservice wg0 | Out-Null
    Start-Sleep -Seconds 1
    & 'C:\Program Files\WireGuard\wireguard.exe' /installtunnelservice $conf | Out-Null
    Start-Sleep -Seconds 2
}
$serverPub = (Get-Content 'C:\litegame\wg\server.pub' -Raw).Trim()
Write-Output 'BEGIN_CONF'
Write-Output '[Interface]'
Write-Output ('PrivateKey = ' + $priv)
Write-Output ('Address = ' + $Ip + '/24')
Write-Output 'MTU = 1420'
Write-Output ''
Write-Output '[Peer]'
Write-Output ('PublicKey = ' + $serverPub)
Write-Output ('Endpoint = ' + $Endpoint)
Write-Output 'AllowedIPs = 10.8.0.0/24'
Write-Output 'PersistentKeepalive = 25'
Write-Output 'END_CONF'
Write-Output ('ADDR ' + $Ip)
"""


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description="Create a WireGuard tester peer")
    parser.add_argument("--name", default="")
    parser.add_argument("--ip", default="")
    parser.add_argument("--endpoint", default="")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ssh-host", default="")
    parser.add_argument("--out-dir", default="")
    return parser.parse_args(argv)


def main(argv=None):
    configure_stdio()
    args = parse_args(argv)
    if not args.name:
        print("[deploy/wg] usage: wg-peer.py --name <tester> [--ip <addr>] [--endpoint <host:port>] [--dry-run]")
        return 2
    if re.fullmatch(r"[A-Za-z0-9_-]{1,32}", args.name) is None:
        print("[deploy/wg] invalid name: use letters, digits, underscore or hyphen (<=32)")
        return 2

    project = repo_root()
    try:
        ssh_host = args.ssh_host.strip() or resolve_ssh_host()
        out_dir = Path(args.out_dir) if args.out_dir else project / "Dist" / "out"
        if not out_dir.is_absolute():
            out_dir = project / out_dir
        out_dir.mkdir(parents=True, exist_ok=True)
        endpoint = args.endpoint
        if not endpoint:
            notes = project / "LocalNotes" / "cloud-host.md"
            if not notes.is_file():
                raise RuntimeError("缺 --endpoint，且找不到 LocalNotes/cloud-host.md（机器私有）")
            match = re.search(r"^\|\s*公网 IP\s*\|\s*`?([0-9.]+)", notes.read_text(encoding="utf-8", errors="replace"), re.M)
            if not match:
                raise RuntimeError("LocalNotes/cloud-host.md 里没找到公网 IP（可用 --endpoint 显式指定）")
            endpoint = match.group(1) + ":51820"

        with tempfile.TemporaryDirectory() as temp:
            remote_file = Path(temp) / "lg-wg-peer-remote.ps1"
            remote_file.write_text(REMOTE_SCRIPT, encoding="ascii")
            result = subprocess.run(["scp", "-q", str(remote_file), f"{ssh_host}:C:/litegame/wg-peer.ps1"])
            if result.returncode != 0:
                raise RuntimeError("scp 远端脚本失败（别名/网络）")
            ssh_args = ["ssh", ssh_host, "powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                        r"C:\litegame\wg-peer.ps1", "-Name", args.name, "-Endpoint", endpoint]
            if args.ip:
                ssh_args.extend(["-Ip", args.ip])
            if args.dry_run:
                ssh_args.append("-DryRun")
            output = subprocess.run(ssh_args, capture_output=True, text=True, encoding="utf-8", errors="replace")
            text = (output.stdout or "") + (output.stderr or "")
        if output.returncode != 0 and "BEGIN_CONF" not in text:
            raise RuntimeError(f"云上执行失败（exit {output.returncode}）：{text[:300]}")
        if "DUPLICATE" in text:
            raise RuntimeError(f"peer 已存在：{args.name}（换名，或先手工从 wg0.conf 移除）")
        if "NO_WG" in text:
            raise RuntimeError(r"云上找不到 C:\Program Files\WireGuard\wg.exe")
        if "NO_CONF" in text:
            raise RuntimeError(r"云上找不到 C:\litegame\wg\wg0.conf")
        match = re.search(r"BEGIN_CONF\r?\n(.*?)\r?\nEND_CONF", text, re.S)
        if not match:
            raise RuntimeError("解析不到客户端配置（云上输出不含 BEGIN_CONF 段）")
        conf_path = out_dir / f"{args.name}.conf"
        conf_path.write_text(match.group(1), encoding="ascii")
        addr = re.search(r"ADDR (\S+)", text)
        info("deploy/wg", f"{args.name} -> {addr.group(1) if addr else ''}（隧道内）")
        info("deploy/wg", f"客户端配置：{conf_path}")
        info("deploy/wg", "DryRun：未改云上配置、也未重装隧道——该 .conf 暂不可用" if args.dry_run else "已写入 wg0.conf 并重装隧道服务（生效）")
        return EXIT_OK
    except Exception as exc:
        return fail("deploy/wg", str(exc))


if __name__ == "__main__":
    sys.exit(main())
