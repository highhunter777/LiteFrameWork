<#
.SYNOPSIS
    云端运维（GM）端点调用器：本机起 SSH 隧道转发 → POST 一个动作 → 关隧道。

.DESCRIPTION
    端点本身只绑云主机 WireGuard 隧道地址 10.8.0.1（公网不可达），所以从开发机走 SSH 端口转发。

    两个本机/协议层的坑已在脚本内规避（详见《云端GM通道专项设计》与 10 手册 §5.1）：
    1. **本机端口**：默认远程端口 9101 **不能**直接用作本机转发端口——它落在 Windows 的
       TCP 排除端口区间（9014-9113，Hyper-V/WinNAT 预留），ssh 会报
       `bind [127.0.0.1]:9101: Permission denied`。故默认本机端口取 19101（-LocalPort 可覆盖）。
    2. **POST 必须带 body**：无 Content-Length 的请求会被 HttpListener 直接 411
       （Length Required）。所有动作统一带 `{}` 或具体 JSON。

    密钥单源：优先本机用户环境变量 LITEGAME_GM_KEY，缺失则回退解析机器私有文件
    LocalNotes/cloud-host.md（该文件被 .gitignore 忽略）。**密钥不入库、脚本不硬编码**。

.EXAMPLE
    pwsh -NoProfile -File scripts/deploy/gm.ps1 -Action rooms
    pwsh -NoProfile -File scripts/deploy/gm.ps1 -Action start -Room Room-A
    pwsh -NoProfile -File scripts/deploy/gm.ps1 -Action kick -Room Room-A -Player 1
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('rooms', 'start', 'abort', 'kick', 'settle-flush', 'rules')]
    [string]$Action,

    [string]$Room,
    [int]$Player = -1,

    # 仅 -Action rules：位掩码（1 = 免死 / 2 = 无限弹药 / 0 = 关闭），或用两个布尔开关
    [int]$Flags = -1,
    [switch]$NoDeath,
    [switch]$InfiniteAmmo,

    [string]$SshHost,
    [string]$RemoteHost = '10.8.0.1',
    [int]$RemotePort = 9101,

    # 默认 19101：9101 在本机属排除端口区间，绑不了（见 .DESCRIPTION）
    [int]$LocalPort = 19101,

    [switch]$KeepTunnel
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_ssh-host.ps1')
if ([string]::IsNullOrWhiteSpace($SshHost)) { $SshHost = Resolve-LiteGameSshHost }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

function Resolve-GmKey {
    $fromEnv = [Environment]::GetEnvironmentVariable('LITEGAME_GM_KEY', 'User')
    if (-not [string]::IsNullOrWhiteSpace($fromEnv)) { return $fromEnv.Trim() }

    # 回退：机器私有文件（不入库）里 ``` 代码块中的 base64 行
    $md = Join-Path $repo 'LocalNotes/cloud-host.md'
    if (-not (Test-Path $md)) { throw "找不到密钥来源：环境变量 LITEGAME_GM_KEY 未设，且 $md 不存在" }
    $inBlock = $false
    foreach ($line in Get-Content $md) {
        if ($line.Trim() -eq '```') {
            if ($inBlock) { break }
            $inBlock = $true
            continue
        }
        if ($inBlock -and $line.Trim() -match '^[A-Za-z0-9+/]{16,}={0,2}$') { return $line.Trim() }
    }
    throw "解析不到密钥：$md 中没有 base64 密钥块（或请设置用户环境变量 LITEGAME_GM_KEY）"
}

$key = Resolve-GmKey

if (($Action -eq 'start' -or $Action -eq 'abort' -or $Action -eq 'kick') -and [string]::IsNullOrWhiteSpace($Room)) {
    throw "动作 $Action 需要 -Room <房号>（先用 -Action rooms 看房号）"
}
if ($Action -eq 'kick' -and $Player -lt 0) { throw "踢席位需要 -Player <席位号>" }

if ($Action -eq 'rules' -and $Flags -lt 0) {
    $Flags = ($(if ($NoDeath) { 1 } else { 0 })) -bor ($(if ($InfiniteAmmo) { 2 } else { 0 }))
}

$body = switch ($Action) {
    'kick' { '{"room":"' + $Room + '","player":' + $Player + '}' }
    'start' { '{"room":"' + $Room + '"}' }
    'abort' { '{"room":"' + $Room + '"}' }
    'rules' { '{"room":"' + $Room + '","flags":' + $Flags + '}' }
    default { '{}' }
}

# ---- 起隧道 ----
$forward = "$LocalPort`:$RemoteHost`:$RemotePort"
$proc = Start-Process -FilePath ssh -ArgumentList '-N', '-L', $forward, $SshHost `
    -PassThru -WindowStyle Hidden
try {
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        $conn = Get-NetTCPConnection -State Listen -LocalPort $LocalPort -ErrorAction SilentlyContinue
        if ($conn) { $ready = $true; break }
    }
    if (-not $ready) { throw "隧道未建立（本机 $LocalPort 未监听）——查 ssh 别名 $SshHost 是否可用" }

    $uri = "http://127.0.0.1:$LocalPort/gm/$Action"
    Write-Host "[gm] POST $uri  body=$body" -ForegroundColor DarkGray
    $output = & curl.exe -s -X POST $uri -H "X-GM-Key: $key" -H 'Content-Type: application/json' --data-binary $body
    Write-Output $output
}
finally {
    if (-not $KeepTunnel) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Write-Host "[gm] 隧道已关闭" -ForegroundColor DarkGray
    }
    else {
        Write-Host "[gm] 隧道保留（ssh pid=$($proc.Id)，本机端口 $LocalPort）" -ForegroundColor DarkGray
    }
}
