<#
.SYNOPSIS
    运维 SSH 目的地解析（**隧道优先**）—— `deploy-room.ps1` / `gm.ps1` / `wg-peer.ps1` 共用。

.DESCRIPTION
    公网 `22/tcp` 的安全组白名单按**来源 IP** 放行，而本机出口 IP 会变（换宽带、切代理、代理
    TUN/全局模式都会变），每次变都要改安全组。WireGuard 隧道内的 22 不受来源 IP 影响
    （安全组只看到封装用的 UDP 51820），故默认走隧道入口。

    解析顺序（先命中先用）：
      1. 环境变量 `LITEGAME_SSH_HOST`（进程级 → 用户级）——显式逃生口，CI/临时换机用；
      2. 探测 `10.8.0.1:22` 且本机 ssh 别名 `litegame-cloud-tunnel` 已指向该地址 → 用它；
      3. 否则回退公网别名 `litegame-cloud`（并打印原因与修复提示）。

    只做一次 TCP 探测（默认 800ms），不引外部依赖、不读任何密钥、不改任何配置。

.EXAMPLE
    . (Join-Path $PSScriptRoot '_ssh-host.ps1')
    $SshHost = Resolve-LiteGameSshHost
#>
function Resolve-LiteGameSshHost {
    param(
        [string]$TunnelAddress = '10.8.0.1',
        [int]$TunnelPort = 22,
        [int]$ProbeTimeoutMs = 800,
        [string]$TunnelAlias = 'litegame-cloud-tunnel',
        [string]$PublicAlias = 'litegame-cloud'
    )

    # 1) 显式指定（逃生口）：进程级优先，其次用户级环境变量
    $explicit = [Environment]::GetEnvironmentVariable('LITEGAME_SSH_HOST', 'Process')
    if ([string]::IsNullOrWhiteSpace($explicit)) {
        $explicit = [Environment]::GetEnvironmentVariable('LITEGAME_SSH_HOST', 'User')
    }
    if (-not [string]::IsNullOrWhiteSpace($explicit)) {
        Write-Host ("[ssh] 目的地（LITEGAME_SSH_HOST 显式指定）：" + $explicit.Trim()) -ForegroundColor DarkGray
        return $explicit.Trim()
    }

    # 2) 隧道入口可达性（TCP 探测；超时即失败，绝不挂住脚本）
    $reachable = $false
    $client = $null
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $task = $client.ConnectAsync($TunnelAddress, $TunnelPort)
        if ($task.Wait($ProbeTimeoutMs) -and $client.Connected) { $reachable = $true }
    }
    catch {
        $reachable = $false
    }
    finally {
        if ($client) { try { $client.Close() } catch { } }
    }

    if (-not $reachable) {
        Write-Host ("[ssh] 隧道 {0}:{1} 不可达 → 回退公网别名 {2}（隧道未起时先看服务 WireGuardTunnel；公网 22 可能被安全组挡）" -f `
            $TunnelAddress, $TunnelPort, $PublicAlias) -ForegroundColor Yellow
        return $PublicAlias
    }

    # 3) 别名必须已指向隧道地址（别名缺失/指错时给出可抄的修复块，而不是让 ssh 报难懂的错）
    $config = (& ssh -G $TunnelAlias 2>$null) -join "`n"
    if ($config -notmatch ('(?m)^hostname\s+' + [regex]::Escape($TunnelAddress) + '\s*$')) {
        Write-Host ("[ssh] 隧道可达，但别名 {0} 未配置为 HostName {1}——在 ~/.ssh/config 补一段" -f $TunnelAlias, $TunnelAddress) -ForegroundColor Yellow
        Write-Host ("      Host {0}`n          HostName {1}`n          User <云主机账号>`n          IdentityFile <私钥路径>`n          IdentitiesOnly yes`n          StrictHostKeyChecking accept-new" -f $TunnelAlias, $TunnelAddress) -ForegroundColor Yellow
        Write-Host ("[ssh] 本次回退 {0}" -f $PublicAlias) -ForegroundColor Yellow
        return $PublicAlias
    }

    Write-Host ("[ssh] 隧道 {0}:{1} 可达 → 用别名 {2}（不受本机出口 IP 影响）" -f $TunnelAddress, $TunnelPort, $TunnelAlias) -ForegroundColor DarkGray
    return $TunnelAlias
}
