<#
.SYNOPSIS
    给测试者开一条 WireGuard 隧道（云上生成密钥 + 追加 peer + 重装隧道服务 → 本机落地 .conf）。

.DESCRIPTION
    替代 10 手册 §4 的手工加 peer（Windows 侧原本没有等价于 Linux `wg-peers.sh` 的脚本）。
    流程：本机 → 上传纯 ASCII 远端脚本 → ssh 执行（云上 wg.exe 生成密钥、追加 [Peer]、重装隧道服务）
    → 解析回传的客户端配置 → 写到本机 `Dist/out/<名字>.conf`（**私钥只落在该文件，不回显**）。

    安全/约束：
    - 名字只允许字母数字下划线短横线（≤32），重名直接拒；
    - 隧道内地址自动递增（10.8.0.2、.3…），也可 -Ip 指定；
    - **会重装隧道服务**（WireGuard 同名服务重装才生效）——隧道内客户端会短暂断线；
    - `-DryRun` 只生成配置、不改云上（用于先验证链路）。

.EXAMPLE
    pwsh -NoProfile -File scripts/deploy/wg-peer.ps1 -Name alice
    pwsh -NoProfile -File scripts/deploy/wg-peer.ps1 -Name bob -Ip 10.8.0.9 -DryRun
#>
[CmdletBinding()]
param(
    [string]$Name = '',
    [string]$Ip = '',
    [string]$Endpoint = '',
    [switch]$DryRun,
    [string]$SshHost,
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Name)) {
    Write-Output '[deploy/wg] usage: wg-peer.ps1 -Name <tester> [-Ip <addr>] [-Endpoint <host:port>] [-DryRun]'
    exit 2
}
if ($Name -notmatch '^[A-Za-z0-9_-]{1,32}$') {
    Write-Output '[deploy/wg] invalid name: use letters, digits, underscore or hyphen (<=32)'
    exit 2
}
. (Join-Path $PSScriptRoot '_ssh-host.ps1')
if ([string]::IsNullOrWhiteSpace($SshHost)) { $SshHost = Resolve-LiteGameSshHost }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'Dist/out' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

if (-not $Endpoint) {
    $md = Join-Path $repo 'LocalNotes/cloud-host.md'
    if (-not (Test-Path $md)) { throw '缺 -Endpoint，且找不到 LocalNotes/cloud-host.md（机器私有）' }
    $hit = Select-String -Path $md -Pattern '^\|\s*公网 IP\s*\|\s*`?([0-9.]+)' | Select-Object -First 1
    if (-not $hit) { throw 'LocalNotes/cloud-host.md 里没找到公网 IP（可用 -Endpoint 显式指定）' }
    $Endpoint = $hit.Matches[0].Groups[1].Value + ':51820'
}

$remoteScript = @'
param(
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
'@

$remoteFile = Join-Path $env:TEMP 'lg-wg-peer-remote.ps1'
Set-Content -Path $remoteFile -Value $remoteScript -Encoding ascii

& scp -q $remoteFile "$SshHost`:C:/litegame/wg-peer.ps1"
if ($LASTEXITCODE -ne 0) { throw 'scp 远端脚本失败（别名/网络）' }

$sshArgs = @('powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    'C:\litegame\wg-peer.ps1', '-Name', $Name, '-Endpoint', $Endpoint)
if ($Ip) { $sshArgs += @('-Ip', $Ip) }
if ($DryRun) { $sshArgs += '-DryRun' }

$out = & ssh $SshHost @sshArgs 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -and $out -notmatch 'BEGIN_CONF') {
    throw "云上执行失败（exit $LASTEXITCODE）：$($out.Split("`n")[0..2] -join ' / ')"
}
if ($out -match 'DUPLICATE') { throw "peer 已存在：$Name（换名，或先手工从 wg0.conf 移除）" }
if ($out -match 'NO_WG') { throw '云上找不到 C:\Program Files\WireGuard\wg.exe' }
if ($out -match 'NO_CONF') { throw '云上找不到 C:\litegame\wg\wg0.conf' }

$m = [regex]::Match($out, 'BEGIN_CONF\r?\n(?<conf>[\s\S]*?)\r?\nEND_CONF')
if (-not $m.Success) { throw '解析不到客户端配置（云上输出不含 BEGIN_CONF 段）' }

$confPath = Join-Path $OutDir ($Name + '.conf')
Set-Content -Path $confPath -Value $m.Groups['conf'].Value -Encoding ascii

$addr = [regex]::Match($out, 'ADDR (\S+)').Groups[1].Value
Write-Host "[deploy/wg] $Name -> $addr（隧道内）" -ForegroundColor Green
Write-Host "[deploy/wg] 客户端配置：$confPath" -ForegroundColor Green
if ($DryRun) {
    Write-Host '[deploy/wg] DryRun：未改云上配置、也未重装隧道——该 .conf 暂不可用' -ForegroundColor Yellow
}
else {
    Write-Host '[deploy/wg] 已写入 wg0.conf 并重装隧道服务（生效）' -ForegroundColor Green
}

# 私钥只在该 .conf 里：一人一份、不回传、不截图
