# ─────────────────────────────────────────────────────────────────────────────
# Player build (C0-2, 2026-09-22)
#
# Per: Client Framework Master Design 19 C0 - "BuildProfile, one-command build,
#      Windows x64 IL2CPP player build" and 14.2 (versioned BuildProfile).
# Chain: restore-check -> l0-dep-scan -> sync pipeline build state to the
#        profile JSON -> pipeline build (confirm) -> poll build_status ->
#        verify artifacts.
# Requires: Unity editor running (Pipeline server) - it executes the build on
#           the live editor; no batchmode second instance needed.
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8), and
#       keep BuildProfiles/*.json ASCII-only for the same reason.
# Known pipeline quirks handled here:
#   - CLI prints a "Command Success Result Parameters" table; the Result JSON
#     is tab-field 3 of the response line (Parameters follows as field 4).
#   - set_player_settings JSON quotes get stripped by PS 5.1 native arg
#     passing, so the backend switch uses eval_file calling the Unity API
#     directly (same pattern as scripts/l2-unity-gate.ps1).
# Usage: powershell -NoProfile -File scripts/build-player.ps1 -Profile BuildProfiles/windows-x64.json
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$Profile = 'BuildProfiles/windows-x64.json',
    [switch]$SkipRestoreCheck
)

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..')).Path
}

function Invoke-Pipeline([string]$command, [string[]]$cmdArgs) {
    # returns the Result JSON string (tab-field 3 of the CLI response table)
    $saved = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        if ($cmdArgs -and $cmdArgs.Count -gt 0) { $out = & unity command $command @cmdArgs --project-path $ProjectPath --proxy-disable 2>&1 }
        else { $out = & unity command $command --project-path $ProjectPath --proxy-disable 2>&1 }
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $saved }
    $text = ($out | ForEach-Object { "$_" }) -join "`n"
    if ($code -ne 0) { throw "pipeline '$command' failed (exit $code): $text" }
    foreach ($line in ($text -split "`n")) {
        $parts = $line -split "`t"
        if ($parts.Count -ge 3 -and $parts[2].StartsWith('{')) { return $parts[2] }
    }
    return $text
}

function Invoke-EvalFile([string]$fileName, [string]$code) {
    # write a no-BOM UTF-8 script into Temp/ and run it on the editor main thread
    $file = Join-Path $ProjectPath "Temp\$fileName"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($file, $code, $utf8NoBom)
    return Invoke-Pipeline 'eval_file' @("Temp/$fileName")
}

# ── 1. gates ─────────────────────────────────────────────────────────────────
if (-not $SkipRestoreCheck) {
    Write-Host '== restore-check ==' -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'restore-check.ps1') -ProjectPath $ProjectPath
    if ($LASTEXITCODE -ne 0) { Write-Host 'restore-check failed - build aborted' -ForegroundColor Red; exit 1 }
    Write-Host '== l0-dep-scan ==' -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'l0-dep-scan.ps1') -ProjectPath $ProjectPath
    if ($LASTEXITCODE -ne 0) { Write-Host 'l0-dep-scan failed - build aborted' -ForegroundColor Red; exit 1 }
}

# ── 2. read BuildProfile ─────────────────────────────────────────────────────
$profilePath = if ([IO.Path]::IsPathRooted($Profile)) { $Profile } else { Join-Path $ProjectPath $Profile }
if (-not (Test-Path $profilePath)) { Write-Host "profile missing: $profilePath" -ForegroundColor Red; exit 1 }
$prof = Get-Content $profilePath -Raw | ConvertFrom-Json
Write-Host "BuildProfile: $($prof.profileName) | platform=$($prof.platform) backend=$($prof.backend) scenes=$(@($prof.scenes).Count)" -ForegroundColor White

# ── 3. sync pipeline state to profile ────────────────────────────────────────
$bs = Invoke-Pipeline 'get_build_settings' $null | ConvertFrom-Json
if ($bs.activeBuildTarget -ne $prof.platform) {
    Write-Host "switching build target -> $($prof.platform)" -ForegroundColor Yellow
    Invoke-Pipeline 'switch_build_target' @('--target', $prof.platform) | Out-Null
    Start-Sleep -Seconds 10
}

$ps = Invoke-Pipeline 'get_player_settings' $null | ConvertFrom-Json
if ($ps.values.scriptingBackend -ne $prof.backend) {
    Write-Host "switching scripting backend -> $($prof.backend) (eval_file; domain reload follows)" -ForegroundColor Yellow
    Invoke-EvalFile 'build-backend-switch.cs' @'
UnityEditor.Build.NamedBuildTarget t = UnityEditor.Build.NamedBuildTarget.Standalone;
UnityEditor.PlayerSettings.SetScriptingBackend(t, UnityEditor.ScriptingImplementation.IL2CPP);
return UnityEditor.PlayerSettings.GetScriptingBackend(t).ToString();
'@ | Out-Null
    Start-Sleep -Seconds 25   # domain reload: pipeline server restarts; wait it out
    $ps2 = Invoke-Pipeline 'get_player_settings' $null | ConvertFrom-Json
    if ($ps2.values.scriptingBackend -ne $prof.backend) { Write-Host 'backend switch did not persist' -ForegroundColor Red; exit 1 }
}

# scenes: ensure profile scenes are in the build list, extras removed
$bs = Invoke-Pipeline 'get_build_settings' $null | ConvertFrom-Json
foreach ($s in $prof.scenes) {
    $has = $bs.scenes | Where-Object { $_.path -eq $s }
    if (-not $has) {
        Write-Host "adding scene to build: $s" -ForegroundColor Yellow
        Invoke-Pipeline 'add_scene_to_build' @('--path', $s) | Out-Null
    }
}
$profileSceneSet = @{}
foreach ($s in $prof.scenes) { $profileSceneSet[$s] = $true }
$bs2 = Invoke-Pipeline 'get_build_settings' $null | ConvertFrom-Json
foreach ($s in $bs2.scenes) {
    if (-not $profileSceneSet.ContainsKey($s.path)) {
        Write-Host "removing scene from build (not in profile): $($s.path)" -ForegroundColor Yellow
        Invoke-Pipeline 'remove_scene_from_build' @('--path', $s.path) | Out-Null
    }
}

# ── 4. dry run validation ────────────────────────────────────────────────────
Write-Host '== build dry_run ==' -ForegroundColor Cyan
$dryJson = Invoke-Pipeline 'build' @('--dry_run', 'true')
$dry = $dryJson | ConvertFrom-Json
if ($dry.status -eq 'error' -or $dry.success -eq $false) {
    Write-Host "dry_run rejected: $($dry.message)" -ForegroundColor Red; exit 1
}
Write-Host '  dry_run OK' -ForegroundColor Green

# ── 5. build (confirm) + poll ────────────────────────────────────────────────
Write-Host '== build (confirm; IL2CPP may take 10-30 min) ==' -ForegroundColor Cyan
$respJson = Invoke-Pipeline 'build' @('--confirm', 'true')
$resp = $respJson | ConvertFrom-Json
if ($resp.status -eq 'error' -or $resp.success -eq $false) {
    Write-Host "build refused: $($resp.message)" -ForegroundColor Red; exit 1
}

$timeoutMin = 45
$polls = 0
$state = 'unknown'
do {
    Start-Sleep -Seconds 20
    $polls++
    try {
        $stJson = Invoke-Pipeline 'build_status' $null
        $st = $stJson | ConvertFrom-Json
        $state = if ($st.status) { $st.status } else { 'unknown' }
        Write-Host ("  [{0:mm\:ss}] build_status: $state" -f [TimeSpan]::FromSeconds($polls * 20)) -ForegroundColor DarkGray
    }
    catch { Write-Host "  [poll $polls] status unavailable ($($_.Exception.Message))" -ForegroundColor DarkGray }
    $done = ($state -match 'completed|succeeded|finished|failed|error|cancelled|aborted')
} while (-not $done -and ($polls * 20) -lt ($timeoutMin * 60))

if (-not $done) { Write-Host "build_status timeout after $timeoutMin min" -ForegroundColor Red; exit 1 }
$finalJson = Invoke-Pipeline 'build_status' $null
if ($finalJson -match '"(failed|error|cancelled|aborted)"\s*:\s*true' -or $finalJson -match 'status..:\s*.(failed|error|cancelled|aborted)') {
    Write-Host "build FAILED: $finalJson" -ForegroundColor Red
    exit 1
}

# ── 6. artifact verification ─────────────────────────────────────────────────
$outDir = Join-Path $ProjectPath $prof.output
$glob = if ($prof.artifactGlob) { $prof.artifactGlob } else { '*.exe' }
$artifact = Get-ChildItem $outDir -Filter $glob -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $artifact) { Write-Host "no artifact ($glob) under $outDir - artifact verification failed" -ForegroundColor Red; exit 1 }
Write-Host "artifact: $($artifact.FullName) ($([math]::Round($artifact.Length/1MB,1)) MB)" -ForegroundColor Green
Write-Host ''
Write-Host 'C0-2 build PASSED' -ForegroundColor Green
exit 0
