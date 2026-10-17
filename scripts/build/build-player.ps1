# ─────────────────────────────────────────────────────────────────────────────
# Player build
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
#     directly (same pattern as scripts/gate/l2-unity-gate.ps1).
# Usage: powershell -NoProfile -File scripts/build/build-player.ps1 -Profile BuildProfiles/windows-x64.json
#        -SkipBundles skips step 3c (builtin content bundles) - code-only iterations only.
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$Profile = 'BuildProfiles/windows-x64.json',
    [switch]$SkipRestoreCheck,
    [switch]$SkipBundles
)

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..\..')).Path
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
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjectPath 'scripts/gate/restore-check.ps1') -ProjectPath $ProjectPath
    if ($LASTEXITCODE -ne 0) { Write-Host 'restore-check failed - build aborted' -ForegroundColor Red; exit 1 }
    Write-Host '== l0-dep-scan ==' -ForegroundColor Cyan
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjectPath 'scripts/gate/l0-dep-scan.ps1') -ProjectPath $ProjectPath
    if ($LASTEXITCODE -ne 0) { Write-Host 'l0-dep-scan failed - build aborted' -ForegroundColor Red; exit 1 }
}

# ── 1.5 buildHash pre-build check (unskippable) ─────────────────────────────
# Recomputes the hash from current sources and compares it with the generated
# constant; stale -> abort. This replaces the retired dev-time BuildHashTests
# (2026-10-07): validation moved out of the dev loop, onto the build gate.
# Never bypass it: a stale constant bakes a wrong version fingerprint into the
# released client and the Join handshake is the only line left after shipping.
Write-Host '== buildhash check ==' -ForegroundColor Cyan
# --check-shared（P1④）：出包门禁只把守**握手红线**那一个常量（共享源集）。Meta 的构建身份
# 是另一个生成目标（Server/MetaServer/Host/BuildHash.g.cs），它是**独立产物**的日志标识、
# 不参与准入判定，Meta 常量陈旧不该拦客户端出包——其把守点 = 发布 Meta 前跑 `--check`（两个都查）。
& python (Join-Path $ProjectPath 'scripts/codegen/gen-build-hash.py') --check-shared
if ($LASTEXITCODE -ne 0) {
    Write-Host 'buildHash stale - run: python scripts/codegen/gen-build-hash.py - build aborted' -ForegroundColor Red
    exit 1
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

# -- 3b. bake deploy.args from profile endpoints --------------------------------
# BuildProfile.endpoints (JSON object) -> Assets/StreamingAssets/deploy.args
# (one "-key=value" per line; client loaders merge cmdline > baked > defaults).
# Empty/absent endpoints -> DELETE the file: a stale baked key from another
# channel would leak into this build (fail-closed hygiene, not a nicety).
# NOTE: values must not contain spaces (token stream form); endpoints are
# urls/base64/ints by contract.
$deployArgsPath = Join-Path $ProjectPath 'Assets\StreamingAssets\deploy.args'
$endpointCount = 0
$endpointText = ''
if ($prof.endpoints -and $prof.endpoints.PSObject.Properties) {
    $endpointCount = @($prof.endpoints.PSObject.Properties).Count
}
if ($endpointCount -gt 0) {
    foreach ($p in $prof.endpoints.PSObject.Properties) {
        if ([string]::IsNullOrWhiteSpace([string]$p.Value)) { continue }
        $v = [string]$p.Value
        if ($v -match '\s') { Write-Host "endpoint '$($p.Name)' has whitespace - refuse to bake (token form)" -ForegroundColor Red; exit 1 }
        $endpointText += "-$($p.Name)=$v`n"
    }
    Write-Host "baking deploy.args ($endpointCount endpoint(s))" -ForegroundColor Yellow
    $bakeScript = 'string abs = System.IO.Path.GetFullPath("Assets/StreamingAssets/deploy.args");' + "`n" +
                  'System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(abs));' + "`n" +
                  'System.IO.File.WriteAllText(abs, @"' + $endpointText.TrimEnd("`n") + '");' + "`n" +
                  'UnityEditor.AssetDatabase.ImportAsset("Assets/StreamingAssets/deploy.args");' + "`n" +
                  'return "baked: " + abs;'
    Invoke-EvalFile 'bake-deploy-args.cs' $bakeScript | Out-Null
}
else {
    if (Test-Path $deployArgsPath) {
        Write-Host 'endpoints empty/absent -> deleting stale deploy.args' -ForegroundColor Yellow
        Invoke-EvalFile 'bake-deploy-args-delete.cs' @'
string rel = "Assets/StreamingAssets/deploy.args";
string abs = System.IO.Path.GetFullPath(rel);
if (System.IO.File.Exists(abs)) { System.IO.File.Delete(abs); UnityEditor.AssetDatabase.Refresh(); }
return "deleted (or absent): " + abs;
'@ | Out-Null
    }
}

# ── 3c. builtin content bundles (Offline) ────────────────────────────────────
# The player reads content (tables/scenes/prefabs/art) through the content system,
# i.e. from YooAsset bundles copied into StreamingAssets/yoo -- NOT from loose files.
# So this must run BEFORE the player build, otherwise the PREVIOUS content is baked in.
# Field evidence (2026-10-17): table tbitemconfig.bytes entered the repo on 10-11 while
# StreamingAssets/yoo still held the 10-03 build -> the shipped exe failed at boot with
# "BOOT FAILED 配置文件加载失败:Assets/GameData/Config/Item/tbitemconfig.bytes".
# Default = always build (a stale package is a silent trap); -SkipBundles is the
# explicit escape hatch for code-only iterations.
if ($SkipBundles) {
    Write-Host '== bundles: SKIPPED (-SkipBundles; packaged content may be stale) ==' -ForegroundColor Yellow
}
elseif ($prof.platform -ne 'StandaloneWindows64') {
    # BuiltinBundleBuildInvoker builds the Windows content set; other targets would need
    # their own invoker call. Fail loud rather than silently shipping the wrong platform.
    Write-Host "== bundles: profile platform is $($prof.platform) - only StandaloneWindows64 content is wired; use -SkipBundles to acknowledge ==" -ForegroundColor Red
    exit 1
}
else {
    Write-Host '== bundles: build builtin content bundles (Offline) ==' -ForegroundColor Cyan
    $bundleCsPath = Join-Path $ProjectPath 'Temp\build-bundles.cs'
    $bundleCs = @'
public static class BuildBundles
{
    public static string Start()
    {
        LiteGame.EditorTools.BuiltinBundleBuildInvoker.RunAsync();
        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status;
    }

    public static string Status()
    {
        return LiteGame.EditorTools.BuiltinBundleBuildInvoker.Status
             + " | out=" + LiteGame.EditorTools.BuiltinBundleBuildInvoker.LastOutputDirectory;
    }
}
'@
    [System.IO.File]::WriteAllText($bundleCsPath, $bundleCs, (New-Object System.Text.UTF8Encoding($false)))
    Invoke-Pipeline 'run_script' @('--file', 'Temp/build-bundles.cs', '--entry', 'BuildBundles.Start') | Out-Null

    # SBP build runs on the editor main thread; poll Status (same contract the invoker
    # documents) instead of blocking the pipeline call beyond its main-thread window.
    $bundleTimeoutSec = 1200
    $bundlePolls = 0
    $bundleState = ''
    while (($bundlePolls * 10) -lt $bundleTimeoutSec) {
        Start-Sleep -Seconds 10
        $bundlePolls++
        $bundleState = Invoke-Pipeline 'run_script' @('--file', 'Temp/build-bundles.cs', '--entry', 'BuildBundles.Status')
        Write-Host ("  [{0:mm\:ss}] {1}" -f [TimeSpan]::FromSeconds($bundlePolls * 10), $bundleState) -ForegroundColor DarkGray
        if ($bundleState -match '"result":"(success|failed)') { break }
    }
    if ($bundleState -notmatch '"result":"success') {
        # Print just the invoker's message line, not the whole pipeline JSON (the failure
        # text carries a full stack trace). "during play mode" is the most common cause:
        # SBP builds cannot run while the editor is playing -- stop Play and retry.
        $detail = if ($bundleState -match '"result":"([^"]*)') { $Matches[1] } else { $bundleState }
        Write-Host "bundles not built: $detail" -ForegroundColor Red
        Write-Host '  hint: if the editor is in Play mode, stop Play and retry (SBP cannot build during play).' -ForegroundColor Yellow
        Write-Host '  to proceed without fresh content anyway: -SkipBundles' -ForegroundColor Yellow
        exit 1
    }
    Write-Host '  bundles OK' -ForegroundColor Green
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
$buildStart = Get-Date
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

# freshness: a build with compile errors can report "completed" without producing anything
# (observed 2026-10-03: stale GameAssembly kept, script said PASSED) - require a file newer
# than this build's start time, else it is a silent no-op.
$freshCut = $buildStart.AddMinutes(-1)
$fresh = Get-ChildItem $outDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -ge $freshCut } | Select-Object -First 1
if (-not $fresh) {
    Write-Host "artifact stale: nothing under $outDir newer than $($buildStart.ToString('HH:mm:ss')) - build produced nothing (check compile errors in Editor.log)" -ForegroundColor Red
    exit 1
}
Write-Host "freshness: $($fresh.Name) @ $($fresh.LastWriteTime.ToString('HH:mm:ss'))" -ForegroundColor Green
Write-Host ''
Write-Host 'Player build PASSED' -ForegroundColor Green
exit 0
