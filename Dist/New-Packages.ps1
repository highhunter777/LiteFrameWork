# -----------------------------------------------------------------------------
# Batch tester packaging (see Dist/README.md).
#
# For every tester name: ensure <name>.conf exists, then call New-Package.ps1 to
# assemble a distribution package. Two modes:
#   Full (default) : every <name>.zip contains the complete game build + conf.
#   -Overlay       : every <name>.zip contains only kit + conf + server.txt (no game);
#                    testers extract the shared LiteGame-base.zip once, then this overlay.
#
# Usage:
#   pwsh -NoProfile -File Dist/New-Packages.ps1 -Testers alice,bob -Zip
#   pwsh -NoProfile -File Dist/New-Packages.ps1 -Testers alice,bob -GenerateConf -Zip
#   pwsh -NoProfile -File Dist/New-Packages.ps1 -Zip                  # scan Dist/out/*.conf
#   pwsh -NoProfile -File Dist/New-Packages.ps1 -Testers alice -Overlay -Zip
#   pwsh -NoProfile -File Dist/New-Packages.ps1 -Testers alice -DryRun
#
# NOTE: -GenerateConf calls scripts/deploy/wg-peer.ps1 (adds peer + reinstalls the
#       cloud tunnel service); that briefly disconnects the existing tunnel.
# Keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
# -----------------------------------------------------------------------------
[CmdletBinding()]
param(
    [string[]]$Testers = @(),
    [string]$ConfDir = 'Dist/out',
    [string]$GameBuild = 'Builds/StandaloneWindows64',
    [string]$OutDir = 'Dist/out',
    [string]$ServerUrl = '',
    [switch]$GenerateConf,
    [switch]$Overlay,
    [switch]$Zip,
    [switch]$Force,
    [switch]$DryRun,
    [switch]$ContinueOnError
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot                 # Dist/ -> repo root
$pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue)
if (-not $pwsh) { $pwsh = (Get-Command powershell).Source } else { $pwsh = $pwsh.Source }
$newPackage = Join-Path $PSScriptRoot 'New-Package.ps1'
$wgPeer = Join-Path $repo 'scripts/deploy/wg-peer.ps1'

function Resolve-FromRoot([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return '' }
    if ([IO.Path]::IsPathRooted($p)) { return $p }
    return (Join-Path $repo $p)
}

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Ok([string]$text) { Say "[ok]   $text" 'Green' }
function Warn([string]$text) { Say "[warn] $text" 'Yellow' }
function Fail([string]$text) { Say "[fail] $text" 'Red' }

$confDirAbs = Resolve-FromRoot $ConfDir
if (-not (Test-Path $confDirAbs)) { throw "conf dir not found: $confDirAbs" }

# `pwsh -File` passes "-Testers a,b" as ONE string (CLI has no array parsing), so split here.
$Testers = @($Testers | ForEach-Object { ([string]$_ -split ',') } | ForEach-Object { $_.Trim() } |
            Where-Object { $_ })

if (-not $Testers -or $Testers.Count -eq 0) {
    $found = @(Get-ChildItem -Path $confDirAbs -Filter *.conf -File -ErrorAction SilentlyContinue |
               Sort-Object Name)
    if ($found.Count -eq 0) {
        throw "no testers given and no *.conf under $confDirAbs"
    }
    $Testers = @($found | ForEach-Object { $_.BaseName })
    Say "scanning $($Testers.Count) conf(s): $($Testers -join ', ')" 'Cyan'
}

if ($Overlay) {
    Warn 'overlay mode: packages contain kit + conf only (no game); send LiteGame-base.zip once separately'
}
if ($GenerateConf) {
    Warn 'GenerateConf will add cloud peers and reinstall the tunnel service (brief disconnect per tester)'
}

$ok = 0
$failed = @()
$seen = @{}
foreach ($t in $Testers) {
    $name = ([string]$t).Trim()
    if ([string]::IsNullOrWhiteSpace($name)) { continue }
    if ($name -notmatch '^[A-Za-z0-9_-]{1,32}$') {
        Fail "invalid tester name: '$name' (letters/digits/_/- only, <=32)"
        $failed += $name
        if (-not $ContinueOnError) { break } else { continue }
    }
    if ($seen.ContainsKey($name)) { continue }
    $seen[$name] = $true

    $conf = Join-Path $confDirAbs ($name + '.conf')
    if (-not (Test-Path $conf)) {
        if (-not $GenerateConf -or $DryRun) {
            Fail "missing conf: $conf (run wg-peer.ps1 -Name $name first, or add -GenerateConf)"
            $failed += $name
            if (-not $ContinueOnError) { break } else { continue }
        }
        Say "== generating conf: $name ==" 'Cyan'
        & $pwsh -NoProfile -File $wgPeer -Name $name -OutDir $confDirAbs
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $conf)) {
            Fail "conf generation failed: $name"
            $failed += $name
            if (-not $ContinueOnError) { break } else { continue }
        }
    }

    Say "== packaging: $name ==" 'Cyan'
    $childArgs = @(
        '-NoProfile', '-File', $newPackage,
        '-Tester', $name,
        '-Conf', $conf,
        '-GameBuild', $GameBuild,
        '-OutDir', $OutDir
    )
    if ($ServerUrl) { $childArgs += @('-ServerUrl', $ServerUrl) }
    if ($Overlay) { $childArgs += '-SkipGameCopy' }
    if ($Zip) { $childArgs += '-Zip' }
    if ($Force) { $childArgs += '-Force' }
    if ($DryRun) { $childArgs += '-CheckOnly' }

    & $pwsh @childArgs
    if ($LASTEXITCODE -ne 0) {
        Fail "package failed: $name"
        $failed += $name
        if (-not $ContinueOnError) { break } else { continue }
    }
    $ok++
}

Say ''
if ($failed.Count -eq 0) {
    Say ("batch packaging OK: {0}/{1}{2}" -f $ok, $seen.Count,
        $(if ($DryRun) { ' (dry-run, nothing written)' } else { '' })) 'Green'
    exit 0
}
Say ("batch packaging finished: ok={0}, failed={1} ({2})" -f $ok, $failed.Count, ($failed -join ', ')) 'Red'
exit 1
