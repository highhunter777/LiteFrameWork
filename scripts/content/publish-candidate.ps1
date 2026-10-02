# ─────────────────────────────────────────────────────────────────────────────
# Candidate publish layouter (CDN channel publish side; Content design §7 deploy contract)
#
# Purpose: lay out a candidate content directory into the **CDN layout** and produce
#          the signed envelope -- matching the URL contract of HttpCandidateProvider /
#          HttpCandidateFetcher (single source):
#              {CdnRoot}/candidate.json              <- envelope (release pointer)
#              {CdnRoot}/{ReleaseId}/{path}          <- immutable files declared by the manifest
#
# Usage:
#   powershell -NoProfile -File scripts/content/publish-candidate.ps1 `
#       -ContentDir <candidate-dir> [-CdnRoot <dir>] [-ReleaseId rel-local-001] [-Revision 1]
#       [-BundleSource <yooasset-package-root>]
#   (CdnRoot empty = scripts/content/local-cdn.json "directory"; address/port come from the
#    same config via local-cdn.config.ps1)
#   (-BundleSource = optional YooAsset package root (e.g. the build output directory of
#    BuiltinBundleBuildInvoker.RunForCandidate); its CONTENTS are mirrored into
#    <ContentDir>/bundle first -- the candidate asset-bundle segment scanned by
#    gen-candidate.ps1 / consumed by the runtime with the confirmed candidate root.)
#
# Serve locally afterwards (static HTTP file service, zero deps):
#   powershell -NoProfile -File scripts/content/serve-cdn.ps1
# Consume (Player command line injects the deploy config, parsed by ContentDeployConfig):
#   Test.exe -content.cdnUrl=http://<bind>:<port>
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8 -- same rule
#   as player-smoke.ps1). Layout only, no signing here -- signing lives in
#   gen-candidate.ps1 (private key on the signing machine; self-checked against the
#   built-in anchor). gen-candidate.ps1 contains non-ASCII text and is therefore
#   invoked via a UTF-8 scriptblock, bypassing the BOM-less parse issue.
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ContentDir,
    [string]$CdnRoot = '',         # empty = from scripts/content/local-cdn.json (directory)
    [string]$ReleaseId = 'rel-local-001',
    [int]$Revision = 1,
    [int]$ExpiresInDays = 30,
    [string]$BundleSource = ''     # optional YooAsset package root -> mirrored into <ContentDir>/bundle
)

$ErrorActionPreference = 'Stop'

function Fail([string]$msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red; exit 1 }
function Ok([string]$msg)   { Write-Host "  [PASS] $msg" -ForegroundColor Green }

if (-not (Test-Path -LiteralPath $ContentDir)) { Fail "candidate dir missing: $ContentDir" }

# 0) optional asset-bundle segment: mirror the package root contents into <ContentDir>/bundle
#    (fresh each run -- the staging dir may be reused across candidate builds; the segment
#    must match exactly what gets signed).
if ($BundleSource) {
    if (-not (Test-Path -LiteralPath $BundleSource)) { Fail "bundle source missing: $BundleSource" }
    $bundleDst = Join-Path (Resolve-Path -LiteralPath $ContentDir).Path 'bundle'
    if (Test-Path -LiteralPath $bundleDst) { Remove-Item -LiteralPath $bundleDst -Recurse -Force }
    New-Item -ItemType Directory -Path $bundleDst -Force | Out-Null
    $bundleSrcFull = (Resolve-Path -LiteralPath $BundleSource).Path
    robocopy $bundleSrcFull $bundleDst /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "robocopy (bundle) failed with exit code $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
    Ok "bundle segment mirrored: $bundleDst"
}

$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$projectRoot = (Resolve-Path (Join-Path $here '..')).Path

# serve directory / address: single source = scripts/content/local-cdn.json (loader fails
# loudly when missing); script parameters still override for one-off runs.
$cdn = & (Join-Path $here 'local-cdn.config.ps1')
$cdnFull = if ($CdnRoot) {
    if ([IO.Path]::IsPathRooted($CdnRoot)) { $CdnRoot } else { Join-Path $projectRoot $CdnRoot }
} else { $cdn.Directory }

# 1) layout: candidate content -> {CdnRoot}/{ReleaseId}/ (wipe the same-ReleaseId dir
#    first; republishing immutable files = same content re-laid; content changes must
#    bump ReleaseId/Revision, never overwrite a published path)
$releaseDir = Join-Path $cdnFull $ReleaseId
if (Test-Path -LiteralPath $releaseDir) { Remove-Item -LiteralPath $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
# robocopy /E mirrors CONTENTS (Copy-Item with an existing destination dir would nest
# the source dir inside it -> URL level off by one). Exit codes 0-7 are success.
$srcFull = (Resolve-Path $ContentDir).Path
robocopy $srcFull $releaseDir /E /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fail "robocopy failed with exit code $LASTEXITCODE" }
$global:LASTEXITCODE = 0
Ok "files laid out: $releaseDir"

# 2) signed envelope -> {CdnRoot}/candidate.json (gen-candidate self-checks against
#    the built-in anchor). UTF-8 scriptblock: PS 5.1 would misparse the BOM-less
#    UTF-8 text of gen-candidate.ps1 as GBK.
$genPath = Join-Path $here 'gen-candidate.ps1'
$genSource = [System.IO.File]::ReadAllText($genPath, [System.Text.Encoding]::UTF8)
$genBlock = [scriptblock]::Create($genSource)
& $genBlock -ContentDir $ContentDir -ReleaseId $ReleaseId -Revision $Revision `
    -ExpiresInDays $ExpiresInDays -OutFile (Join-Path $cdnFull 'candidate.json')
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) { Fail "gen-candidate failed (see above) -- envelope not produced" }

Write-Host ''
Write-Host "publish done: $cdnFull" -ForegroundColor Cyan
Write-Host "  serve:   powershell -NoProfile -File scripts/content/serve-cdn.ps1   (config: $($cdn.ConfigPath))" -ForegroundColor Gray
Write-Host "  consume: Test.exe -content.cdnUrl=http://$($cdn.Bind):$($cdn.Port)" -ForegroundColor Gray
exit 0
