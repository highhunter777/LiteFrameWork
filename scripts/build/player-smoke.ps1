# ─────────────────────────────────────────────────────────────────────────────
# Player startup smoke
#
# Per: Client Framework Master Design 19 C0 - "startup smoke gate" and
#      "player can reach the minimal error/Patch UI".
# Flow: ensure no stale instance -> fresh Player.log -> launch built exe ->
#       poll log for a defined-state marker within timeout -> kill process.
# Defined-state markers (either passes):
#   - BootstrapError : preload failed and the built-in minimal error UI shown
#   - [Asset] ready  : offline bundles present and AssetService fully initialized
#   - [UI] main open : real main page opened (U1 full-chain anchor)
#
# NOTE: markers must be completion evidence - do not add bare identifiers
#   (e.g. 'AssetService') as markers; they can match a stack-trace line and pass
#   while the app is still starting up, masking a hang.
# Any other outcome (process died early, no marker in timeout) fails.
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
# Usage: powershell -NoProfile -File scripts/build/player-smoke.ps1 [-TimeoutSec 30]
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$ExePath = 'Builds/StandaloneWindows64/Test.exe',
    [int]$TimeoutSec = 30,
    [string[]]$PlayerArgs = @(),   # forwarded to the player exe (e.g. -content.cdnUrl=http://127.0.0.1:18090)
    [switch]$MemLoop,              # memory-loop mode: wait for '[MemLoop] done' then assert trend/residency from log lines
    [int]$MemLoopCycles = 30,      # must match MemoryLoopProbe.Cycles
    [double]$MemLoopMaxGrowthMb = 8.0,
    [switch]$InputModal            # modal-input-recovery mode: wait for '[InputModal] done' then assert phase lines
)

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..\..')).Path
}

$exe = if ([IO.Path]::IsPathRooted($ExePath)) { $ExePath } else { Join-Path $ProjectPath $ExePath }
if (-not (Test-Path $exe)) {
    Write-Host "player exe missing: $exe (run scripts/build/build-player.ps1 first)" -ForegroundColor Red
    exit 1
}

# company/product from ProjectSettings.asset -> Player.log path
$settings = Get-Content (Join-Path $ProjectPath 'ProjectSettings\ProjectSettings.asset') -Raw
$company = 'DefaultCompany'
$product = 'Test'
if ($settings -match 'companyName:\s*(\S+)') { $company = $Matches[1] }
if ($settings -match 'productName:\s*(\S+)') { $product = $Matches[1] }
$playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\$company\$product\Player.log"
Write-Host "Player smoke | exe: $exe" -ForegroundColor White
Write-Host "Player.log: $playerLog" -ForegroundColor DarkGray

# kill stale instance and start with a fresh log
Get-Process -Name (Split-Path $exe -Leaf) -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
if (Test-Path $playerLog) { Remove-Item $playerLog -Force -ErrorAction SilentlyContinue }

$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -PassThru -ArgumentList $PlayerArgs

$markers = @('BootstrapError', '[Asset] ready', '[UI] main open')
if ($MemLoop) {
    # MemLoop mode: probe waits for content-ready itself (retry loop up to 180s inside the
    # player), so the outer poll needs a much larger budget than a plain startup smoke.
    if ($TimeoutSec -eq 30) { $TimeoutSec = 240 }
    $markers = @('[MemLoop] fail', '[MemLoop] done')
}
if ($InputModal) {
    # InputModal mode: probe retries modal open until content-ready (up to 180s inside the player).
    if ($TimeoutSec -eq 30) { $TimeoutSec = 240 }
    $markers = @('[InputModal] fail', '[InputModal] done')
}
Write-Host "launched (pid $($proc.Id)); mode=$(if ($MemLoop) { 'memloop' } else { 'startup' }); polling up to $TimeoutSec s" -ForegroundColor White
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$hit = $null
$lastLog = ''
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    if ($proc.HasExited) {
        # Probe modes (MemLoop/InputModal) self-quit on done/fail: the marker may already be in
        # the log. Check it before declaring early exit, or a completed probe run reads as FAIL.
        if (Test-Path $playerLog) {
            $lastLog = Get-Content $playerLog -Raw -ErrorAction SilentlyContinue
            foreach ($m in $markers) {
                if ($lastLog -match [regex]::Escape($m)) { $hit = $m; break }
            }
        }
        if (-not $hit) { Write-Host "process exited early (code $($proc.ExitCode))" -ForegroundColor Red }
        break
    }
    if (Test-Path $playerLog) {
        $lastLog = Get-Content $playerLog -Raw -ErrorAction SilentlyContinue
        foreach ($m in $markers) {
            # [regex]::Escape: markers are LITERAL strings. '[Asset] ready' as a raw regex is a char
            # class ('[Asset]' matches one of A/s/e/t) that never matches its own literal.
            if ($lastLog -match [regex]::Escape($m)) { $hit = $m; break }
        }
        if ($hit) { break }
    }
}

$alive = -not $proc.HasExited
if ($alive) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

# ── MemLoop assertions: parse '[MemLoop] cycle=...' lines and enforce trend + residency ──
function Assert-MemLoopLog([string]$logText) {
    $pattern = '\[MemLoop\] cycle=(\d+) allocMB=([\d.]+) monoMB=([\d.]+) active=(\d+) pooled=(\d+) held=(\d+)'
    $rows = @()
    foreach ($line in ($logText -split "`n")) {
        if ($line -match $pattern) {
            $rows += ,@([int]$Matches[1], [double]$Matches[2], [int]$Matches[4], [int]$Matches[5], [int]$Matches[6])
        }
    }
    if ($rows.Count -ne $MemLoopCycles) {
        Write-Host "  [FAIL] expected $MemLoopCycles cycle lines, found $($rows.Count)" -ForegroundColor Red
        return $false
    }
    # Residency cap: single entry location -> active always 0 after Hide, pooled/held pinned at 1.
    # (Update the pinned values together with ContentSampleAssets.EntryLocations growth.)
    foreach ($r in $rows) {
        if ($r[2] -ne 0 -or $r[3] -ne 1 -or $r[4] -ne 1) {
            Write-Host "  [FAIL] residency drifted at cycle $($r[0]): active=$($r[2]) pooled=$($r[3]) held=$($r[4])" -ForegroundColor Red
            return $false
        }
    }
    $first = $rows[0..4]   | ForEach-Object { $_[1] } | Measure-Object -Average
    $last  = $rows[-5..-1] | ForEach-Object { $_[1] } | Measure-Object -Average
    $growth = [math]::Round($last.Average - $first.Average, 2)
    Write-Host ("  mem trend: first5avg={0}MB last5avg={1}MB growth={2}MB (budget {3}MB)" -f `
        [math]::Round($first.Average,1), [math]::Round($last.Average,1), $growth, $MemLoopMaxGrowthMb)
    if ($growth -gt $MemLoopMaxGrowthMb) {
        Write-Host "  [FAIL] memory growth over budget" -ForegroundColor Red
        return $false
    }
    return $true
}

# ── InputModal assertions: require all three phase lines with their expected verdicts ──
function Assert-InputModalLog([string]$logText) {
    # Phase lines (literal markers, one per probe phase; 'ok' suffix = all frames consistent):
    #   baseline  : not blocked, held intent visible every frame
    #   modal     : blocked by the product-level 'ui.modal' source, pending zeroed, gate counter advanced
    #   recovered : first post-close sample frame already carries the held intent (no phantom zero frame)
    $required = @(
        '\[InputModal\] baseline ok frames=\d+ blocked=0 move=1\.00 fire=1',
        '\[InputModal\] modal-open form=\d+ isOpen=True topModal=\d+ blockers=\d+',
        '\[InputModal\] modal ok frames=\d+ by=ui\.modal zero=1 disposed=\+\d+',
        '\[InputModal\] modal-close form=\d+ isOpen=False modal=False',
        '\[InputModal\] recovered ok frames=\d+ blocked=0 move=1\.00 fire=1 firstFrameZero=0'
    )
    foreach ($pat in $required) {
        if ($logText -notmatch $pat) {
            Write-Host "  [FAIL] missing/incorrect phase line: $pat" -ForegroundColor Red
            return $false
        }
    }
    if ($logText -match '\[InputModal\] fail') {
        Write-Host "  [FAIL] probe reported failure:" -ForegroundColor Red
        ($logText -split "`n" | Where-Object { $_ -match '\[InputModal\] fail' }) | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed }
        return $false
    }
    return $true
}

Write-Host ''
if ($hit) {
    if ($MemLoop) {
        if ($hit -eq '[MemLoop] fail') {
            Write-Host '  [FAIL] probe reported failure:' -ForegroundColor Red
            if ($lastLog) { ($lastLog -split "`n" | Select-Object -Last 20) | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed } }
            Write-Host 'memloop smoke FAILED' -ForegroundColor Red
            exit 1
        }
        if (-not (Assert-MemLoopLog $lastLog)) {
            Write-Host 'memloop smoke FAILED' -ForegroundColor Red
            exit 1
        }
    }
    if ($InputModal) {
        if ($hit -eq '[InputModal] fail') {
            Write-Host '  [FAIL] probe reported failure:' -ForegroundColor Red
            if ($lastLog) { ($lastLog -split "`n" | Select-Object -Last 20) | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed } }
            Write-Host 'inputmodal smoke FAILED' -ForegroundColor Red
            exit 1
        }
        if (-not (Assert-InputModalLog $lastLog)) {
            Write-Host 'inputmodal smoke FAILED' -ForegroundColor Red
            exit 1
        }
    }
    Write-Host "  [OK] defined-state marker reached: '$hit' (process alive: $alive)" -ForegroundColor Green
    if ($MemLoop) { Write-Host 'memloop smoke PASSED' -ForegroundColor Green }
    elseif ($InputModal) { Write-Host 'inputmodal smoke PASSED' -ForegroundColor Green }
    else { Write-Host 'player smoke PASSED' -ForegroundColor Green }
    exit 0
}
Write-Host '  [FAIL] no defined-state marker within timeout - last log lines:' -ForegroundColor Red
if ($lastLog) { ($lastLog -split "`n" | Select-Object -Last 40) | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed } }
else { Write-Host '    (Player.log missing or empty)' -ForegroundColor DarkRed }
Write-Host 'player smoke FAILED' -ForegroundColor Red
exit 1
