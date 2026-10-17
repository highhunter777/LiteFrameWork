# 测试者一键联机环境（WireGuard 隧道 + 连通自检）
#
# 《上云联机测试》§1.1 访问模型 B 的分发面：把本脚本、你那份 `*.conf`（隧道配置）、
# `server.txt`（一行 Meta 基址）与游戏一起发给测试者，他双击同目录的「加入联机环境.cmd」即可。
#
# 本文件是 UTF-8 **带 BOM**——仓库其它脚本坚持纯 ASCII（PS 5.1 会把无 BOM 的 UTF-8 当 GBK 解析，
# 中文注释直接崩），本脚本面向中文测试者故用 BOM + 中文输出；**改动时务必保留 BOM**。
# 本脚本不含任何密钥：隧道私钥只在测试者本机的 .conf 里（那份文件不要回传、不要入库）。
[CmdletBinding()]
param(
    [switch]$CheckOnly,        # 体检模式：只报告状态，不改动系统
    [switch]$NoPause,          # 结束后不停留（给自动化调用）
    [string]$Server = '',      # Meta 基址；缺省读同目录 server.txt
    [string]$TunnelConf = '',  # 隧道配置；缺省自动找同目录唯一的 *.conf
    [string]$InstallerPath = '' # WireGuard 安装包；缺省自动找同目录 wireguard*.exe|*.msi
)

$ErrorActionPreference = 'Stop'
# 本脚本会刻意执行"预期失败"的命令（sc query 查不存在的服务、winget 报已安装）并自行判读退出码，
# 故关掉 7.3+ 的"原生命令非零即抛"行为（5.1 无此变量，Get-Variable 兜住）。
if (Get-Variable PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$wgDir = Join-Path $env:ProgramFiles 'WireGuard'
$wgExe = Join-Path $wgDir 'wireguard.exe'

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Step([string]$text) { Say "`n== $text" 'Cyan' }
function Ok([string]$text) { Say "[完成] $text" 'Green' }
function Warn([string]$text) { Say "[注意] $text" 'Yellow' }
function Fail([string]$text) { Say "[失败] $text" 'Red' }
function Stop-Here([string]$text) {
    Fail $text
    Say '把这一屏截图发给发你游戏的人，他会告诉你怎么处理。' 'Yellow'
    if (-not $NoPause) { [void](Read-Host '按回车关闭') }
    exit 1
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-TunnelConfPath {
    if ($TunnelConf) {
        if (-not (Test-Path $TunnelConf)) { Stop-Here "指定的隧道配置不存在：$TunnelConf" }
        return (Resolve-Path $TunnelConf).Path
    }
    $confs = @(Get-ChildItem -Path $scriptDir -Filter *.conf -File -ErrorAction SilentlyContinue)
    if ($confs.Count -eq 0) { Stop-Here "同目录没找到隧道配置（*.conf）——请向发你游戏的人要一份。" }
    if ($confs.Count -gt 1) {
        Stop-Here ("同目录有多份 .conf，无法判断用哪一份：" + ($confs.Name -join '、') + "。删掉多余的再跑。")
    }
    return $confs[0].FullName
}

function Get-ServerUrl {
    if ($Server) { return $Server.TrimEnd('/') }
    $f = Join-Path $scriptDir 'server.txt'
    if (-not (Test-Path $f)) { return '' }
    foreach ($line in Get-Content $f) {
        $t = $line.Trim()
        if ($t -and -not $t.StartsWith('#')) { return $t.TrimEnd('/') }
    }
    return ''
}

function Get-LocalInstaller {
    if ($InstallerPath) {
        if (Test-Path $InstallerPath) { return (Resolve-Path $InstallerPath).Path }
        Stop-Here "指定的安装包不存在：$InstallerPath"
    }
    $c = Get-ChildItem -Path $scriptDir -File -ErrorAction SilentlyContinue |
         Where-Object { $_.Name -match '(?i)^wireguard.*\.(exe|msi)$' } | Select-Object -First 1
    if ($c) { return $c.FullName }
    return ''
}

function Test-TunnelRunning([string]$name) {
    $out = & sc.exe query "WireGuardTunnel`$$name" 2>&1 | Out-String
    return ($out -match 'RUNNING')
}

function Test-MetaAlive([string]$url) {
    try {
        $r = Invoke-WebRequest -Uri "$url/live" -TimeoutSec 10 -UseBasicParsing
        return ($r.StatusCode -eq 200)
    } catch { return $false }
}

function Show-GameExeHint {
    $exes = @(Get-ChildItem -Path $scriptDir -Filter *.exe -File -ErrorAction SilentlyContinue |
              Where-Object { $_.Name -notmatch '(?i)^wireguard' })
    if ($exes.Count -gt 0) { Say ("现在双击游戏：{0}" -f $exes[0].FullName) 'Green' }
    else { Say '现在双击游戏 exe 即可（就在发你的那个文件夹里）。' 'Green' }
}

# ── 0. 头 ──────────────────────────────────────────────────────────────────

Say '联机测试环境准备（WireGuard 隧道）' 'White'
Say "目录：$scriptDir"
if ($CheckOnly) { Warn '体检模式：只报告状态，不改动系统。' }

# ── 1. 权限 ────────────────────────────────────────────────────────────────

Step '检查管理员权限'
if (Test-Admin) {
    Ok '已是管理员权限。'
}
elseif ($CheckOnly) {
    Warn '当前不是管理员——真正执行时需要（安装软件与隧道服务都要）。'
}
else {
    Warn '需要管理员权限，正在弹出 UAC 确认框（请点"是"）。'
    $argLine = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($Server) { $argLine += " -Server `"$Server`"" }
    if ($TunnelConf) { $argLine += " -TunnelConf `"$TunnelConf`"" }
    if ($InstallerPath) { $argLine += " -InstallerPath `"$InstallerPath`"" }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argLine | Out-Null
    exit 0
}

# ── 2. 同目录资源 ──────────────────────────────────────────────────────────

Step '检查同目录的配置'
$confPath = Get-TunnelConfPath
$tunnelName = [IO.Path]::GetFileNameWithoutExtension($confPath)
Ok "隧道配置：$confPath（隧道名 $tunnelName）"

$serverUrl = Get-ServerUrl
if ($serverUrl) { Ok "服务器地址：$serverUrl" }
else { Warn '没有 server.txt 也没给 -Server：装完隧道后不做连通自检（游戏内仍会用包里的地址）。' }

# ── 3. WireGuard 本体 ──────────────────────────────────────────────────────

Step '检查 WireGuard 客户端'
$installed = Test-Path $wgExe
if ($installed) {
    Ok "已安装：$wgExe"
}
elseif ($CheckOnly) {
    $local = Get-LocalInstaller
    $wingetExit = 0
    if ($local) { Warn "未安装；执行时会用同目录安装包：$([IO.Path]::GetFileName($local))" }
    elseif (Get-Command winget -ErrorAction SilentlyContinue) { Warn '未安装；执行时会用 winget 安装（WireGuard.WireGuard）' }
    else { Warn '未安装，且同目录无安装包、系统也没有 winget——执行会失败，需先手动装。' }
}
else {
    $local = Get-LocalInstaller
    if ($local) {
        Say "安装：$([IO.Path]::GetFileName($local))"
        if ($local -match '(?i)\.msi$') {
            $p = Start-Process msiexec.exe -ArgumentList @('/i', "`"$local`"", '/qn', '/norestart') -Wait -PassThru
        }
        else {
            $p = Start-Process $local -ArgumentList '/S' -Wait -PassThru   # 官方 exe 安装包支持 /S；不支持则下面复核会拦住
        }
        Say "安装程序退出码：$($p.ExitCode)"
    }
    elseif (Get-Command winget -ErrorAction SilentlyContinue) {
        Say '同目录没有安装包，改用 winget 联网安装（国内访问官方源常失败）……'
        & winget install --id WireGuard.WireGuard -e --silent --accept-package-agreements --accept-source-agreements
        $wingetExit = $LASTEXITCODE
    }
    else {
        Stop-Here '没有可用的安装方式：同目录没有 wireguard 安装包，系统也没有 winget。'
    }

    if (-not (Test-Path $wgExe)) {
        if ($wingetExit -ne 0) {
            Stop-Here ("winget 安装失败（退出码 $wingetExit）——最常见原因是下不动官方源（国内不稳）。请让发你包的人把 wireguard 安装包放进本文件夹，再跑一次本脚本。")
        }
        Stop-Here '安装后仍找不到 wireguard.exe——可能被杀毒软件拦截了，或安装程序需要你手动点几下。'
    }
    Ok "安装完成：$wgExe"
}

# ── 4. 隧道服务 ────────────────────────────────────────────────────────────

Step '检查隧道服务'
if ($CheckOnly) {
    if (Test-TunnelRunning $tunnelName) {
        Warn "隧道已在运行（体检模式不动它；真正执行时会先卸后装，确保用的是包内这份 conf）。"
    }
    else {
        Warn "隧道尚未安装（执行时会执行：卸同名服务(若有) + wireguard.exe /installtunnelservice `"$confPath`"）。"
    }
}
else {
    # 无条件先卸后装：换服务器/换配置时同名服务已存在（install 会失败），且旧配置不会自动更新；
    # 重装一次是幂等的，也是唯一能保证"用的是包内这份 conf"的做法。
    $existing = & sc.exe query "WireGuardTunnel`$$tunnelName" 2>&1 | Out-String
    if ($existing -match 'SERVICE_NAME') {
        Say '检测到同名隧道已存在，先卸掉再装……'
        & $wgExe /uninstalltunnelservice $tunnelName | Out-Null
        Start-Sleep -Seconds 2
    }
    Say '把隧道装成系统服务（开机自启，不用开界面）……'
    & $wgExe /installtunnelservice $confPath
    $up = $false
    for ($i = 0; $i -lt 12; $i++) {
        Start-Sleep -Milliseconds 700
        if (Test-TunnelRunning $tunnelName) { $up = $true; break }
    }
    if (-not $up) {
        Fail '隧道服务没能起来。'
        Say '兜底办法：双击同目录里那份 .conf 文件，弹出"导入隧道？"时点"是"，然后在 WireGuard 界面里点"激活"。' 'Yellow'
        Stop-Here '如果连界面也起不来，把这一屏截图发给我。'
    }
    Ok '隧道已建立。'
}

# ── 5. 连通自检 ────────────────────────────────────────────────────────────

if ($serverUrl) {
    Step "连通自检（$serverUrl/live）"
    if ($CheckOnly) {
        Warn '体检模式：跳过实际探测（隧道没在跑，探了必然失败，报"连不上"会误导）。'
    }
    elseif (Test-MetaAlive $serverUrl) {
        Ok '服务器可达，环境就绪。'
    }
    else {
        Fail '隧道看起来起来了，但连不到服务器。'
        Say '按顺序自查：① 隧道服务是否显示 RUNNING（上面那步）；② 换个网络试试（公司网常封 UDP 出站）；③ 把这一屏截图发我。' 'Yellow'
        if (-not $NoPause) { [void](Read-Host '按回车关闭') }
        exit 1
    }
}

if ($CheckOnly) {
    Say ''
    if ($installed -and (Test-TunnelRunning $tunnelName)) {
        Say '── 体检结论：已就绪（WireGuard 已装、隧道在跑）──' 'Green'
        Show-GameExeHint
    }
    else {
        Say '── 体检结论：尚未就绪（本次未改动系统）──' 'Yellow'
        Say '双击「加入联机环境.cmd」把上面标 [注意] 的项做掉即可（约 1 分钟）。'
    }
    if (-not $NoPause) { [void](Read-Host '按回车关闭') }
    exit 0
}

Say ''
Say '── 全部就绪 ──' 'Green'
Show-GameExeHint
Say '（想彻底卸载时：设置→应用里卸载 WireGuard，再删掉那份 .conf 即可。）'
if (-not $NoPause) { [void](Read-Host '按回车关闭') }
