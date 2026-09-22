# ─────────────────────────────────────────────────────────────────────────────
# Player startup smoke (C0-3, 2026-09-22)
#
# Per: Client Framework Master Design 19 C0 - "startup smoke gate" and
#      "player can reach the minimal error/Patch UI".
# Flow: ensure no stale instance -> fresh Player.log -> launch built exe ->
#       poll log for a defined-state marker within timeout -> kill process.
# Defined-state markers (either passes):
#   - BootstrapError : preload failed and the built-in minimal error UI shown
#   - AssetService   : offline bundles present and AssetService initialized
# Any other outcome (process died early, no marker in timeout) fails.
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
# Usage: powershell -NoProfile -File scripts/player-smoke.ps1 [-TimeoutSec 30]
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = '',
    [string]$ExePath = 'Builds/StandaloneWindows64/Test.exe',
    [int]$TimeoutSec = 30
)

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..')).Path
}

$exe = if ([IO.Path]::IsPathRooted($ExePath)) { $ExePath } else { Join-Path $ProjectPath $ExePath }
if (-not (Test-Path $exe)) {
    Write-Host "player exe missing: $exe (run scripts/build-player.ps1 first)" -ForegroundColor Red
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

$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -PassThru
Write-Host "launched (pid $($proc.Id)); polling up to $TimeoutSec s for defined-state marker" -ForegroundColor White

$markers = @('BootstrapError', 'AssetService')
$deadline = (Get-Date).AddSeconds($TimeoutSec)
$hit = $null
$lastLog = ''
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    if ($proc.HasExited) {
        Write-Host "process exited early (code $($proc.ExitCode))" -ForegroundColor Red
        break
    }
    if (Test-Path $playerLog) {
        $lastLog = Get-Content $playerLog -Raw -ErrorAction SilentlyContinue
        foreach ($m in $markers) {
            if ($lastLog -match $m) { $hit = $m; break }
        }
        if ($hit) { break }
    }
}

$alive = -not $proc.HasExited
if ($alive) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }

Write-Host ''
if ($hit) {
    Write-Host "  [OK] defined-state marker reached: '$hit' (process alive: $alive)" -ForegroundColor Green
    Write-Host 'C0-3 player smoke PASSED' -ForegroundColor Green
    exit 0
}
Write-Host '  [FAIL] no defined-state marker within timeout - last log lines:' -ForegroundColor Red
if ($lastLog) { ($lastLog -split "`n" | Select-Object -Last 40) | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkRed } }
else { Write-Host '    (Player.log missing or empty)' -ForegroundColor DarkRed }
Write-Host 'C0-3 player smoke FAILED' -ForegroundColor Red
exit 1
