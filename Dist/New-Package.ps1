# ─────────────────────────────────────────────────────────────────────────────
# Tester package assembly (see Dist/README.md).
#
# Bundles one tester's distribution folder: the game build (flattened to the
# package root), the tester's WireGuard .conf, server.txt, and the two onboarding
# files -- then optionally zips it.
#
# Why not just copy files by hand: three things are easy to get wrong, and each
# is checked here -- forgetting the .conf, leaving server.txt at the wrong
# address, and shipping a .conf whose AllowedIPs is 0.0.0.0/0 (that routes the
# tester's ENTIRE internet traffic through our tunnel; hard-blocked unless -Force).
#
# Usage (from anywhere; paths resolve against the repo root):
#   pwsh -NoProfile -File Dist/New-Package.ps1 -Tester alice -Conf C:\path\alice.conf -ServerUrl http://10.8.0.1:5000 -Zip
#   pwsh -NoProfile -File Dist/New-Package.ps1 -Tester alice -Conf ... -ServerUrl ... -CheckOnly
#   The WireGuard installer (wireguard*.msi|exe) sitting next to this script is bundled into
#   every package -- see Dist/README.md for why (download.wireguard.com is unreliable in CN).
#
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 parses non-BOM UTF-8 as
#       GBK on zh-CN systems and breaks on CJK text.
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Tester,
    [string]$Conf = '',
    [string]$ServerUrl = '',
    [string]$GameBuild = 'Builds/StandaloneWindows64',
    [string]$OutDir = 'Dist/out',
    [switch]$Zip,
    [switch]$Force,
    [switch]$CheckOnly,
    [switch]$SkipGameCopy,
    [switch]$SkipInstaller,
    [switch]$Base
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot          # Dist/ -> repo root

function Say([string]$t, [string]$c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Fail([string]$t) { Say "[FAIL] $t" 'Red'; exit 1 }
function Ok([string]$t) { Say "[ok]   $t" 'Green' }
function Warn([string]$t) { Say "[warn] $t" 'Yellow' }

function Resolve-FromRoot([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return '' }
    if ([IO.Path]::IsPathRooted($p)) { return $p }
    return (Join-Path $repoRoot $p)
}

# -- 1. the kit itself -------------------------------------------------------
# The launcher's name is resolved from disk (not written here twice): it is CJK, and
# this file stays ASCII-only -- plus a rename then needs no edit here.
$launchers = @(Get-ChildItem -Path $PSScriptRoot -Filter *.cmd -File -ErrorAction SilentlyContinue)
if ($launchers.Count -eq 0) { Fail 'launcher (*.cmd) missing next to this script' }
if ($launchers.Count -gt 1) { Fail ('more than one launcher next to this script: ' + ($launchers.Name -join ', ')) }
$kitFiles = @($launchers[0].Name, 'Join-LiteGameTest.ps1')
$missing = @($kitFiles | Where-Object { -not (Test-Path (Join-Path $PSScriptRoot $_)) })
if ($missing.Count -gt 0) { Fail ("kit files missing next to this script: " + ($missing -join ', ')) }
Ok "kit files present ($($kitFiles.Count)): $($kitFiles -join ', ')"

# -- 2. tunnel config --------------------------------------------------------
$confPath = ''
if ($Base) {
    # Base package: one game build for every tester; each tester drops their own
    # unique .conf next to the launcher (WireGuard peers cannot be shared).
    if ($Conf) { Warn '-Base ignores -Conf (base package intentionally contains no tunnel config)' }
    Warn 'base mode: NO tunnel config embedded; each tester must place their own *.conf next to the launcher'
}
else {
    if (-not $Conf) { Fail '-Conf is required (or use -Base to build the shared game package).' }
    $confPath = Resolve-FromRoot $Conf
    if (-not (Test-Path $confPath)) { Fail "tunnel config not found: $confPath" }
    $confText = Get-Content $confPath -Raw
    if ($confText -notmatch '(?m)^\s*\[Interface\]') { Fail "not a WireGuard config (no [Interface]): $confPath" }
    if ($confText -notmatch '(?m)^\s*\[Peer\]') { Fail "not a WireGuard config (no [Peer]): $confPath" }
    if ($confText -notmatch '(?m)^\s*PrivateKey\s*=') { Fail "config has no PrivateKey line: $confPath" }

    # split-tunnel guard: 0.0.0.0/0 (or ::/0) drags the tester's whole internet into our tunnel.
    $badAllowed = @()
    foreach ($m in [regex]::Matches($confText, '(?m)^\s*AllowedIPs\s*=\s*(.+)$')) {
        foreach ($cidr in ($m.Groups[1].Value -split ',')) {
            $c = $cidr.Trim()
            if ($c -eq '0.0.0.0/0' -or $c -eq '::/0') { $badAllowed += $c }
        }
    }
    if ($badAllowed.Count -gt 0) {
        if (-not $Force) {
            Fail ("AllowedIPs contains $($badAllowed -join ', ') -- that routes the tester's ENTIRE traffic through this tunnel. " +
                  'Use the VPN subnet only (e.g. AllowedIPs = 10.8.0.0/24). Pass -Force only if you really mean it.')
        }
        Warn "AllowedIPs contains $($badAllowed -join ', ') -- overridden by -Force."
    }
    Ok "tunnel config ok: $([IO.Path]::GetFileName($confPath))"
}

# -- 3. server address -------------------------------------------------------
if (-not $ServerUrl) {
    $serverTxt = Join-Path $PSScriptRoot 'server.txt'
    if (Test-Path $serverTxt) {
        foreach ($line in Get-Content $serverTxt) {
            $t = $line.Trim()
            if ($t -and -not $t.StartsWith('#')) { $ServerUrl = $t; break }
        }
    }
}
if (-not $ServerUrl) { Fail 'no server address: pass -ServerUrl or create Dist/server.txt (copy server.txt.example).' }
$ServerUrl = $ServerUrl.TrimEnd('/')
if ($ServerUrl -notmatch '^https?://') { Fail "server address must start with http:// or https:// -- got '$ServerUrl'" }
Ok "server address: $ServerUrl"

# -- 4. game build -----------------------------------------------------------
$buildPath = Resolve-FromRoot $GameBuild
# Unity itself names these "do not ship"; on 2026-10-17 the backup folder alone was
# 1.7 GB of a 2.1 GB output -- copying that into every tester package is pure waste.
$shipExcludes = @('*_BackUpThisFolder_ButDontShipItWithYourGame', '*_BurstDebugInformation_DoNotShip')
if ($SkipGameCopy) { Warn "game build copy skipped (-SkipGameCopy); pointing at $buildPath anyway" }
else {
    if (-not (Test-Path $buildPath)) { Fail "game build not found: $buildPath (build it first, or pass -GameBuild <path>)" }
    $exes = @(Get-ChildItem -Path $buildPath -Filter *.exe -File -ErrorAction SilentlyContinue |
              Where-Object { $_.Name -notmatch '(?i)^UnityCrashHandler' })
    if ($exes.Count -eq 0) { Fail "no .exe in $buildPath -- is that really a player build?" }
    Ok "game build: $buildPath ($($exes.Count) exe, ex: $($exes[0].Name))"
}

# -- 4b. bundled WireGuard installer -----------------------------------------
# Testers in mainland China cannot reliably reach download.wireguard.com (fields
# report: winget fails with 0x80072efd, and the same host times out from our cloud
# host too). So the installer is fetched ONCE and shipped inside every package;
# the onboarding script prefers a local installer over any download.
$installer = ''
if (-not $SkipInstaller) {
    $found = @(Get-ChildItem -Path $PSScriptRoot -File -ErrorAction SilentlyContinue |
               Where-Object { $_.Name -match '(?i)^wireguard.*\.(msi|exe)$' } |
               Sort-Object Name)
    if ($found.Count -eq 0) {
        Warn 'no WireGuard installer next to this script -- testers will have to download it (unreliable in CN). See Dist/README.md.'
    }
    else {
        if ($found.Count -gt 1) { Warn ("more than one installer here, using the first: " + ($found.Name -join ', ')) }
        $installer = $found[0]
        Ok ("bundled installer: {0} ({1} MB)" -f $installer.Name, [math]::Round($installer.Length / 1MB, 1))
    }
}

# -- 5. assemble -------------------------------------------------------------
$pkgDir = Join-Path (Resolve-FromRoot $OutDir) $Tester
if ($CheckOnly) {
    Say ''
    Say "-- check-only: nothing written --" 'Cyan'
    Say "would assemble into: $pkgDir"
    Say ("would contain: game build" + $(if ($SkipGameCopy) { ' (skipped)' } else { '' }) +
        $(if ($confPath) { " + $([IO.Path]::GetFileName($confPath))" } else { ' + (each tester supplies own *.conf)' }) +
        " + server.txt + $($kitFiles -join ' + ')" +
        $(if ($installer) { " + $($installer.Name)" } else { '' }))
    exit 0
}

if (Test-Path $pkgDir) {
    if (-not $Force) { Fail "output folder already exists: $pkgDir (use -Force to overwrite, or pick another -Tester name)" }
    Remove-Item $pkgDir -Recurse -Force
}
New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null

if (-not $SkipGameCopy) {
    Say 'copying game build (this is the slow part)...'
    $skippedItems = @()
    foreach ($item in (Get-ChildItem -Path $buildPath -Force)) {
        $drop = $false
        foreach ($pat in $shipExcludes) { if ($item.Name -like $pat) { $drop = $true; break } }
        if ($drop) { $skippedItems += $item.Name; continue }
        Copy-Item -Path $item.FullName -Destination $pkgDir -Recurse -Force
    }
    if ($skippedItems.Count -gt 0) {
        Warn ("not shipped (Unity marks these do-not-ship): " + ($skippedItems -join ', '))
    }
}
foreach ($f in $kitFiles) { Copy-Item -Path (Join-Path $PSScriptRoot $f) -Destination $pkgDir -Force }
if ($installer) { Copy-Item -Path $installer.FullName -Destination $pkgDir -Force }
else { Warn 'package has NO bundled WireGuard installer -- the tester side will try winget (often fails in CN)' }
if ($confPath) {
    Copy-Item -Path $confPath -Destination (Join-Path $pkgDir (Split-Path $confPath -Leaf)) -Force
}
[IO.File]::WriteAllText((Join-Path $pkgDir 'server.txt'), $ServerUrl + "`r`n")
Ok "assembled: $pkgDir"

$zipPath = ''
if ($Zip) {
    $zipPath = Join-Path (Resolve-FromRoot $OutDir) ($Tester + '.zip')
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Say 'zipping...'
    Compress-Archive -Path (Join-Path $pkgDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Ok "zip: $zipPath"
}

# -- 6. manifest + hand-off notes -------------------------------------------
$files = Get-ChildItem -Path $pkgDir -Recurse -File
$sizeMb = [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 1)
Say ''
Say '-- package --' 'Cyan'
Say ("  tester    : {0}" -f $Tester)
Say ("  folder    : {0}" -f $pkgDir)
if ($Zip) { Say ("  zip       : {0}" -f $zipPath) }
Say ("  installer : {0}" -f $(if ($installer) { $installer.Name } else { "(none -- see Dist/README.md)" }))
Say ("  files     : {0} ({1} MB)" -f $files.Count, $sizeMb)
Say ("  send this : {0}" -f $(if ($Zip) { Split-Path $zipPath -Leaf } else { $pkgDir }))
Say ''
Say '-- before sending --' 'Cyan'
if ($Base) {
    Say '  1. send this base package ONCE; each tester gets only their own <name>.conf separately'
    Say '  2. tester: unzip base, put exactly ONE *.conf next to the launcher, then double-click the launcher'
    Say '  3. never send two testers the same .conf (WireGuard peer identity + tunnel address must be unique)'
}
else {
    Say '  1. that .conf is THIS tester''s (one private key per person -- never re-send the same .conf to two people)'
    Say '  2. tell him: unblock the downloaded file (right-click > Properties > Unblock) if Windows blocks it'
    Say ("  3. tell him: double-click the launcher ({0}) first, then the game exe" -f $kitFiles[0])
    Say '  4. keep the first tester on the line -- the install path is untested on a clean machine (see Dist/README.md)'
}
