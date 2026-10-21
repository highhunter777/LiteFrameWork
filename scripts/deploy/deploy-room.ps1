<#
.SYNOPSIS
    一键重新部署云上房间服：发布 → 打包 → 停服 → 上传 → 解压 → 起服 → 回读判据。

.DESCRIPTION
    把 10 手册 §2/§5.1 里的手工步骤收成一条命令。已固化的三个坑：
    1. **先停服再解压**——运行中的 DLL 被占用会 `Can't unlink already-existing object`；
    2. **停/起分两步 ssh**——"停服+解压+起服"合成一次调用时曾出现"无输出且未生效"，分步稳定；
    3. **云上脚本纯 ASCII**——PS 5.1 经 SSH 执行无 BOM 的 UTF-8 中文会按 GBK 误解而解析失败。

.PARAMETER SkipPublish
    跳过 `dotnet publish`，只上传现有 `Builds/win-server/room`（纯配置/重试场景更快）。

.PARAMETER WithConfig
    同时把 `deploy/roomserver.windows.json` 传成云上 `roomserver.json`（模板/端口变更时用）。

.PARAMETER SshHost
    SSH 目的地别名；**留空 = 自动解析**（隧道 `10.8.0.1:22` 可达就用 `litegame-cloud-tunnel`，
    否则回退 `litegame-cloud`）——公网 22 的来源 IP 白名单会随本机出口变化而失效，隧道入口不受影响。
    解析器与 `gm.ps1`/`wg-peer.ps1` 共用：`scripts/deploy/_ssh-host.ps1`。

.EXAMPLE
    pwsh -NoProfile -File scripts/deploy/deploy-room.ps1
    pwsh -NoProfile -File scripts/deploy/deploy-room.ps1 -SkipPublish -WithConfig
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish,
    [switch]$WithConfig,
    # 跳过玩法表同步（默认同步：只换二进制不换表 = StartGame.ConfigHash 与客户端不等，整局被拒）
    [switch]$SkipTables,
    [string]$SshHost
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_ssh-host.ps1')
if ([string]::IsNullOrWhiteSpace($SshHost)) { $SshHost = Resolve-LiteGameSshHost }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$serverRoot = Join-Path $repo 'Builds/win-server'
$roomDir = Join-Path $serverRoot 'room'
$tgz = Join-Path $serverRoot 'room.tgz'
$tablesTgz = Join-Path $serverRoot 'GameData-Config.tgz'

if (-not (Test-Path $roomDir) -and $SkipPublish) {
    throw "找不到 $roomDir：先跑一次不带 -SkipPublish 的部署（会发布产物）"
}

if (-not $SkipPublish) {
    Write-Host '[deploy/room] dotnet publish（win-x64 自包含）...' -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo 'Server/RoomServer/RoomServer.csproj') `
        -c Release -r win-x64 --self-contained true -o $roomDir --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }
}

Write-Host '[deploy/room] 打包 room.tgz ...' -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $serverRoot | Out-Null
# 显式用 Windows System32\tar.exe：Git Bash 的 /usr/bin/tar 会抢 PATH，把 E:\ 当远端主机。
$tarExe = Join-Path $env:SystemRoot 'System32\tar.exe'
if (-not (Test-Path $tarExe)) { $tarExe = 'tar' }
& $tarExe -czf $tgz -C $serverRoot room
if ($LASTEXITCODE -ne 0) { throw "tar 打包失败（exit $LASTEXITCODE）" }

# 玩法表随部署同步（判定面 + UI，共 12 个文件 ≈8KB）：服务端启动期装载，改表不重传 = 数据面身份不符。
# 2026-10-19 实测：Map/tbmap.bytes 改了没传 → 服务端基摘要 0xb0102ea9 vs 客户端 0xa8a7e328，StartGame 被拒。
if (-not $SkipTables) {
    Write-Host '[deploy/room] 打包玩法表 GameData/Config ...' -ForegroundColor Cyan
    & $tarExe -czf $tablesTgz -C (Join-Path $repo 'Assets/GameData/Config') .
    if ($LASTEXITCODE -ne 0) { throw "tar 打包玩法表失败（exit $LASTEXITCODE）" }
}

# 云上脚本：纯 ASCII（避免 PS 5.1 的 GBK 误解），由本机 here-string 原样落盘
$stopScript = @'
Get-Process -Name RoomServer -ErrorAction SilentlyContinue | Stop-Process -Force
Stop-ScheduledTask -TaskName 'LiteGame-Room' -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
Write-Output ('stopped=' + ($null -eq (Get-Process -Name RoomServer -ErrorAction SilentlyContinue)))
'@
$startScript = @'
C:\Windows\System32\tar.exe -xzf C:\litegame\room.tgz -C C:\litegame
Write-Output ('extract=' + $LASTEXITCODE)
if (Test-Path 'C:\litegame\GameData-Config.tgz') {
    New-Item -ItemType Directory -Force -Path 'C:\litegame\GameData\Config' | Out-Null
    C:\Windows\System32\tar.exe -xzf C:\litegame\GameData-Config.tgz -C C:\litegame\GameData\Config
    Write-Output ('tables=' + $LASTEXITCODE)
}
Start-ScheduledTask -TaskName 'LiteGame-Room'
Start-Sleep -Seconds 8
Write-Output ('task=' + (Get-ScheduledTask -TaskName 'LiteGame-Room').State)
Select-String -Path 'C:\litegame\room\out.log' -Pattern 'buildHash=' | Select-Object -Last 1 | ForEach-Object { $_.Line }
Select-String -Path 'C:\litegame\room\out.log' -Pattern '0x[0-9a-fA-F]{8}' | Select-Object -Last 1 | ForEach-Object { $_.Line }
'@
$stopFile = Join-Path $env:TEMP 'lg-room-stop.ps1'
$startFile = Join-Path $env:TEMP 'lg-room-start.ps1'
Set-Content -Path $stopFile -Value $stopScript -Encoding ascii
Set-Content -Path $startFile -Value $startScript -Encoding ascii

Write-Host '[deploy/room] 停服 ...' -ForegroundColor Cyan
& scp -q $stopFile "$SshHost`:C:/litegame/room-stop.ps1"
if ($LASTEXITCODE -ne 0) { throw 'scp 停服脚本失败' }
& ssh $SshHost powershell -NoProfile -ExecutionPolicy Bypass -File C:\litegame\room-stop.ps1
if ($LASTEXITCODE -ne 0) { throw '停服失败' }

Write-Host '[deploy/room] 上传产物 ...' -ForegroundColor Cyan
& scp -q $tgz "$SshHost`:C:/litegame/room.tgz"
if ($LASTEXITCODE -ne 0) { throw 'scp 产物失败（网络/别名/磁盘）' }

if ($WithConfig) {
    Write-Host '[deploy/room] 上传房间配置 ...' -ForegroundColor Cyan
    & scp -q (Join-Path $repo 'deploy/roomserver.windows.json') "$SshHost`:C:/litegame/room/roomserver.json"
    if ($LASTEXITCODE -ne 0) { throw 'scp 配置失败' }
}

if (-not $SkipTables) {
    Write-Host '[deploy/room] 上传玩法表 ...' -ForegroundColor Cyan
    & scp -q $tablesTgz "$SshHost`:C:/litegame/GameData-Config.tgz"
    if ($LASTEXITCODE -ne 0) { throw 'scp 玩法表失败' }
}
else {
    # 跳过表同步时清掉云上的旧表包：否则起服脚本会把上一批的表解压回来（静默回退）
    & ssh $SshHost powershell -NoProfile -Command "Remove-Item C:/litegame/GameData-Config.tgz -ErrorAction SilentlyContinue"
}

Write-Host '[deploy/room] 解压并起服 ...' -ForegroundColor Cyan
& scp -q $startFile "$SshHost`:C:/litegame/room-start.ps1"
& ssh $SshHost powershell -NoProfile -ExecutionPolicy Bypass -File C:\litegame\room-start.ps1
if ($LASTEXITCODE -ne 0) { throw '起服失败' }

Remove-Item $stopFile, $startFile -Force -ErrorAction SilentlyContinue
Write-Host '[deploy/room] 完成：判据 = 上方 task=Running + buildHash 行（应与客户端包内常量同值）' -ForegroundColor Green
