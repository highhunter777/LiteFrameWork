# ─────────────────────────────────────────────────────────────────────────────
# buildHash refresh
#
# Why this exists: buildHash is the Join handshake version red line
# (ServerHost.cs: `join.BuildHash != ServerBuildHash` -> reject). It covers the
# three things that must be byte-identical on both ends: Sim decision source
# (LiteSim/Core/Scripts|Systems), protocol (LiteNet/Proto|Protocol) and the
# table data on both ends.
#
# So after touching Sim/protocol/tables you must run all three:
#   1. rerun the generator (regen BuildHash.g.cs constant)
#   2. rebuild RoomServer (ServerBuildHash is the same constant)
#   3. recompile Unity (client bakes the new constant into its assembly)
# Miss any one -> join is rejected; and the symptom surfaces 10 seconds later
# as a Join timeout, far away from the real cause (forgot to rebuild).
# This script fuses the three into one command and prints a two-end reconcile,
# so "forgot a step" cannot stay silent.
#
# Design constraint (Hot-update design doc section 5): buildHash is a strict
# gate, it must NOT be deleted or loosened. This script only refreshes and
# reconciles; it provides no bypass switch.
#
# Usage:
#   powershell -NoProfile -File scripts/refresh-hash.ps1
#   powershell -NoProfile -File scripts/refresh-hash.ps1 -SkipServerBuild
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$Config = 'Config/roomserver.json',
    [switch]$SkipServerBuild,
    [switch]$SkipUnityRecompile
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..')).Path
}

$repoRoot = $ProjectPath

# -- 1. rerun the buildHash generator ----------------------------------------
Write-Host '== 1/4 rerun buildHash generator ==' -ForegroundColor Cyan
$genScript = Join-Path $repoRoot 'scripts/gen-build-hash.py'
& python $genScript
if ($LASTEXITCODE -ne 0) { Write-Host 'gen-build-hash.py failed' -ForegroundColor Red; exit 1 }

$clientHashFile = Join-Path $repoRoot 'Assets/LiteNet/Protocol/BuildHash.g.cs'
$clientHash = (Select-String -Path $clientHashFile -Pattern '"([0-9a-f]{16})"' |
    Select-Object -First 1).Matches[0].Groups[1].Value
Write-Host "  client constant client=$clientHash" -ForegroundColor Green

# -- 2. rebuild RoomServer (ServerBuildHash is the same constant) ------------
if (-not $SkipServerBuild) {
    Write-Host '== 2/4 rebuild RoomServer ==' -ForegroundColor Cyan
    $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-cli'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & dotnet build (Join-Path $repoRoot 'RoomServer/RoomServer.csproj') -v:q --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host 'RoomServer build failed' -ForegroundColor Red; exit 1 }
}
else {
    Write-Host '== 2/4 skip RoomServer build (-SkipServerBuild) ==' -ForegroundColor DarkGray
}

# -- 3. trigger Unity recompile (client bakes the new constant) --------------
if (-not $SkipUnityRecompile) {
    Write-Host '== 3/4 trigger Unity recompile ==' -ForegroundColor Cyan
    $portFile = Join-Path $repoRoot 'Library/Pipeline/.unity-pipeline-port'
    if (-not (Test-Path $portFile)) {
        Write-Host '  editor not running (no Pipeline port) - recompile Unity manually before joining' -ForegroundColor Yellow
    }
    else {
        $status = ''
        $saved = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & unity command recompile --project-path $repoRoot 2>&1 | Out-Null
            $waited = 0
            do {
                Start-Sleep -Seconds 3
                $waited += 3
                $status = (& unity command recompile_status --project-path $repoRoot 2>&1) -join ' '
            } while ($status -match '"status":"(compiling|triggered)"' -and $waited -lt 180)
        }
        finally { $ErrorActionPreference = $saved }

        if ($status -match '"compilationFailed":true') {
            Write-Host "  Unity compile failed: $status" -ForegroundColor Red
            exit 1
        }
        Write-Host '  Unity recompile done' -ForegroundColor Green
    }
}
else {
    Write-Host '== 3/4 skip Unity recompile (-SkipUnityRecompile) ==' -ForegroundColor DarkGray
}

# -- 4. two-end reconcile (really start the server, read its self-reported hash)
Write-Host '== 4/4 two-end hash reconcile ==' -ForegroundColor Cyan
$serverHash = $null
try {
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        # Start the server, read its self-reported hash, let --duration retire it
        # (no process kill needed). Args go through an array: a bare `--` in the
        # command line is parsed by PS as the decrement operator.
        $runArgs = @(
            'run', '--project', (Join-Path $repoRoot 'RoomServer/RoomServer.csproj'), '--no-build', '--',
            '--config', (Join-Path $repoRoot $Config), '--duration', '1500', '--quiet'
        )
        $out = & dotnet @runArgs 2>&1
    }
    finally { $ErrorActionPreference = $saved }

    $m = ($out -join "`n") | Select-String -Pattern 'buildHash=([0-9a-f]{16})'
    if ($m) { $serverHash = $m.Matches[0].Groups[1].Value }
}
catch {
    Write-Host "  server hash probe incomplete: $($_.Exception.Message)" -ForegroundColor Yellow
}

if ($null -eq $serverHash) {
    Write-Host '  could not read server hash (server not up / port busy) - start it manually to confirm' -ForegroundColor Yellow
    Write-Host "  client constant = $clientHash" -ForegroundColor Green
    exit 0
}

Write-Host "  client = $clientHash"
Write-Host "  server = $serverHash"
if ($clientHash -eq $serverHash) {
    Write-Host '  OK: both ends match - join will be accepted' -ForegroundColor Green
    exit 0
}

Write-Host '  MISMATCH: join will be rejected (ServerHost buildHash red line)' -ForegroundColor Red
exit 1
