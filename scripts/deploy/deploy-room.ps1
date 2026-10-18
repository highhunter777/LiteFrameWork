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

.EXAMPLE
    pwsh -NoProfile -File scripts/deploy/deploy-room.ps1
    pwsh -NoProfile -File scripts/deploy/deploy-room.ps1 -SkipPublish -WithConfig
#>
param(
    [switch]$SkipPublish,
    [switch]$WithConfig,
    [string]$SshHost = 'litegame-cloud'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$serverRoot = Join-Path $repo 'Builds/win-server'
$roomDir = Join-Path $serverRoot 'room'
$tgz = Join-Path $serverRoot 'room.tgz'

if (-not (Test-Path $roomDir) -and $SkipPublish) {
    throw "找不到 $roomDir：先跑一次不带 -SkipPublish 的部署（会发布产物）"
}

if (-not $SkipPublish) {
    Write-Host '[deploy] dotnet publish（win-x64 自包含）...' -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo 'Server/RoomServer/RoomServer.csproj') `
        -c Release -r win-x64 --self-contained true -o $roomDir --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）" }
}

Write-Host '[deploy] 打包 room.tgz ...' -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $serverRoot | Out-Null
& tar -czf $tgz -C $serverRoot room
if ($LASTEXITCODE -ne 0) { throw "tar 打包失败（exit $LASTEXITCODE）" }

# 云上脚本：纯 ASCII（避免 PS 5.1 的 GBK 误解），由本机 here-string 原样落盘
$stopScript = @'
Get-Process -Name RoomServer -ErrorAction SilentlyContinue | Stop-Process -Force
Stop-ScheduledTask -TaskName 'LiteGame-Room' -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3
Write-Output ('stopped=' + ($null -eq (Get-Process -Name RoomServer -ErrorAction SilentlyContinue)))
'@
$startScript = @'
tar -xzf C:\litegame\room.tgz -C C:\litegame
Write-Output ('extract=' + $LASTEXITCODE)
Start-ScheduledTask -TaskName 'LiteGame-Room'
Start-Sleep -Seconds 8
Write-Output ('task=' + (Get-ScheduledTask -TaskName 'LiteGame-Room').State)
Select-String -Path 'C:\litegame\room\out.log' -Pattern 'buildHash=' | Select-Object -Last 1 | ForEach-Object { $_.Line }
'@
$stopFile = Join-Path $env:TEMP 'lg-room-stop.ps1'
$startFile = Join-Path $env:TEMP 'lg-room-start.ps1'
Set-Content -Path $stopFile -Value $stopScript -Encoding ascii
Set-Content -Path $startFile -Value $startScript -Encoding ascii

Write-Host '[deploy] 停服 ...' -ForegroundColor Cyan
& scp -q $stopFile "$SshHost`:C:/litegame/room-stop.ps1"
if ($LASTEXITCODE -ne 0) { throw 'scp 停服脚本失败' }
& ssh $SshHost powershell -NoProfile -ExecutionPolicy Bypass -File C:\litegame\room-stop.ps1
if ($LASTEXITCODE -ne 0) { throw '停服失败' }

Write-Host '[deploy] 上传产物 ...' -ForegroundColor Cyan
& scp -q $tgz "$SshHost`:C:/litegame/room.tgz"
if ($LASTEXITCODE -ne 0) { throw 'scp 产物失败（网络/别名/磁盘）' }

if ($WithConfig) {
    Write-Host '[deploy] 上传房间配置 ...' -ForegroundColor Cyan
    & scp -q (Join-Path $repo 'deploy/roomserver.windows.json') "$SshHost`:C:/litegame/room/roomserver.json"
    if ($LASTEXITCODE -ne 0) { throw 'scp 配置失败' }
}

Write-Host '[deploy] 解压并起服 ...' -ForegroundColor Cyan
& scp -q $startFile "$SshHost`:C:/litegame/room-start.ps1"
& ssh $SshHost powershell -NoProfile -ExecutionPolicy Bypass -File C:\litegame\room-start.ps1
if ($LASTEXITCODE -ne 0) { throw '起服失败' }

Remove-Item $stopFile, $startFile -Force -ErrorAction SilentlyContinue
Write-Host '[deploy] 完成：判据 = 上方 task=Running + buildHash 行（应与客户端包内常量同值）' -ForegroundColor Green
