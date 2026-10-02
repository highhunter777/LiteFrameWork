# ─────────────────────────────────────────────────────────────────────────────
# Local CDN server (serve the publish root over static HTTP, zero dependencies).
#
# Reads scripts/content/local-cdn.json (single source: directory / bind / port) via
# local-cdn.config.ps1; parameters below OVERRIDE the config for one run.
#
# Usage:
#   powershell -NoProfile -File scripts/content/serve-cdn.ps1              # per config
#   powershell -NoProfile -File scripts/content/serve-cdn.ps1 -Port 18091  # one-off override
#
# Prereq: publish root populated -- scripts/content/publish-candidate.ps1 (else 404s).
# Consume: Test.exe -content.cdnUrl=http://<bind>:<port>   (deploy config contract)
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ConfigPath = '',      # override config file (default: scripts/content/local-cdn.json)
    [int]$Port = 0,                # override config port
    [string]$Bind = '',            # override config bind address
    [string]$Directory = ''        # override config serve directory
)

$ErrorActionPreference = 'Stop'

$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$projectRoot = (Resolve-Path (Join-Path $here '..\..')).Path

$cfg = & (Join-Path $here 'local-cdn.config.ps1')
$dir = if ($Directory) { $Directory } else { $cfg.Directory }
$bind = if ($Bind) { $Bind } else { $cfg.Bind }
$port = if ($Port -gt 0) { $Port } else { $cfg.Port }

if (-not [System.IO.Path]::IsPathRooted($dir)) { $dir = Join-Path $projectRoot $dir }
if (-not (Test-Path -LiteralPath $dir)) {
    Write-Host "  [FAIL] serve directory missing: $dir (run scripts/content/publish-candidate.ps1 first)" -ForegroundColor Red
    exit 1
}

$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $python) {
    Write-Host "  [FAIL] python not found in PATH (the local CDN is python -m http.server, stdlib only)" -ForegroundColor Red
    exit 1
}

Write-Host "Local CDN | serving: $dir" -ForegroundColor White
Write-Host "Local CDN | listen:  http://$bind`:$port  (Ctrl+C stops)" -ForegroundColor White
Write-Host "Local CDN | consume: Test.exe -content.cdnUrl=http://$bind`:$port" -ForegroundColor DarkGray

& python -m http.server $port --directory $dir --bind $bind
exit $LASTEXITCODE
