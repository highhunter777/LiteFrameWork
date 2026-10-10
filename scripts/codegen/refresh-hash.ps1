# buildHash refresh orchestration.
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
# Toolbox entry:  pwsh scripts/codegen/toolbox.ps1 refresh-hash [-SkipServerBuild] [-SkipUnityRecompile]
# Direct entry:   pwsh scripts/codegen/refresh-hash.ps1 [-ProjectPath <path>] [-Config <path>] [-SkipServerBuild] [-SkipUnityRecompile]
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$Config = 'Config/roomserver.json',
    [switch]$SkipServerBuild,
    [switch]$SkipUnityRecompile
)

. (Join-Path $PSScriptRoot '_common.ps1')

function Invoke-RefreshHash {
    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        $ProjectPath = Get-CodegenRepoRoot
    }

    $repoRoot = $ProjectPath

    # -- 1. rerun the buildHash generator ----------------------------------------
    Write-Tool 'refresh-hash' 'step 1/4 rerun buildHash generator'
    $genScript = Join-Path $repoRoot 'scripts/codegen/gen-build-hash.py'
    & python $genScript
    if ($LASTEXITCODE -ne 0) { Write-ToolFail 'refresh-hash' 'gen-build-hash.py failed'; exit 1 }

    $clientHashFile = Join-Path $repoRoot 'Assets/LiteNet/Protocol/BuildHash.g.cs'
    $clientHash = (Select-String -Path $clientHashFile -Pattern '"([0-9a-f]{16})"' |
        Select-Object -First 1).Matches[0].Groups[1].Value
    Write-Tool 'refresh-hash' "client constant client=$clientHash"

    # -- 2. rebuild RoomServer (ServerBuildHash is the same constant) ------------
    if (-not $SkipServerBuild) {
        Write-Tool 'refresh-hash' 'step 2/4 rebuild RoomServer'
        $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-cli'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        & dotnet build (Join-Path $repoRoot 'Server/RoomServer/RoomServer.csproj') -v:q --nologo
        if ($LASTEXITCODE -ne 0) { Write-ToolFail 'refresh-hash' 'RoomServer build failed'; exit 1 }
    }
    else {
        Write-Tool 'refresh-hash' 'step 2/4 skip RoomServer build (-SkipServerBuild)'
    }

    # -- 3. trigger Unity recompile (client bakes the new constant) --------------
    if (-not $SkipUnityRecompile) {
        Write-Tool 'refresh-hash' 'step 3/4 trigger Unity recompile'
        $portFile = Join-Path $repoRoot 'Library/Pipeline/.unity-pipeline-port'
        if (-not (Test-Path $portFile)) {
            Write-ToolWarn 'refresh-hash' 'editor not running (no Pipeline port) - recompile Unity manually before joining'
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
                Write-ToolFail 'refresh-hash' "Unity compile failed: $status"
                exit 1
            }
            Write-Tool 'refresh-hash' 'Unity recompile done'
        }
    }
    else {
        Write-Tool 'refresh-hash' 'step 3/4 skip Unity recompile (-SkipUnityRecompile)'
    }

    # -- 4. two-end reconcile (really start the server, read its self-reported hash)
    Write-Tool 'refresh-hash' 'step 4/4 two-end hash reconcile'
    $serverHash = $null
    try {
        $saved = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            # Start the server, read its self-reported hash, let --duration retire it
            # (no process kill needed). Args go through an array: a bare `--` in the
            # command line is parsed by PS as the decrement operator.
            $runArgs = @(
                'run', '--project', (Join-Path $repoRoot 'Server/RoomServer/RoomServer.csproj'), '--no-build', '--',
                '--config', (Join-Path $repoRoot $Config), '--duration', '1500', '--quiet'
            )
            $out = & dotnet @runArgs 2>&1
        }
        finally { $ErrorActionPreference = $saved }

        $m = ($out -join "`n") | Select-String -Pattern 'buildHash=([0-9a-f]{16})'
        if ($m) { $serverHash = $m.Matches[0].Groups[1].Value }
    }
    catch {
        Write-ToolWarn 'refresh-hash' "server hash probe incomplete: $($_.Exception.Message)"
    }

    if ($null -eq $serverHash) {
        Write-ToolWarn 'refresh-hash' 'could not read server hash (server not up / port busy) - start it manually to confirm'
        Write-Tool 'refresh-hash' "client constant = $clientHash"
        exit 0
    }

    Write-Tool 'refresh-hash' "client = $clientHash"
    Write-Tool 'refresh-hash' "server = $serverHash"
    if ($clientHash -eq $serverHash) {
        Write-Tool 'refresh-hash' 'OK: both ends match - join will be accepted'
        exit 0
    }

    Write-ToolFail 'refresh-hash' 'MISMATCH: join will be rejected (ServerHost buildHash red line)'
    exit 1
}

try { Invoke-RefreshHash; exit 0 }
catch { Write-ToolFail 'refresh-hash' $_.Exception.Message; exit 1 }
