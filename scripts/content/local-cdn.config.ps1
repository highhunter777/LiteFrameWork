# ─────────────────────────────────────────────────────────────────────────────
# Local CDN config loader (shared by publish-candidate.ps1 / serve-cdn.ps1).
#
# Single source of the config FILE PATH and of value resolution:
#   scripts/content/local-cdn.json  { "directory", "bind", "port" }
#     - directory: CDN publish root (relative to the project root, or absolute)
#     - bind:      listen address  (server side)
#     - port:      listen port     (server side; consume URL = http://bind:port)
#
# Returns [pscustomobject] with Directory (ABSOLUTE path), Bind, Port.
# Fails loudly when the config file is missing -- it is the single source;
# silent fallback defaults would drift from it. Callers may still override
# values via their own script parameters.
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
# ─────────────────────────────────────────────────────────────────────────────
$ErrorActionPreference = 'Stop'

$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$projectRoot = (Resolve-Path (Join-Path $here '..\..')).Path
$cfgPath = Join-Path $here 'local-cdn.json'

if (-not (Test-Path -LiteralPath $cfgPath)) {
    throw "local CDN config missing: $cfgPath (it is the single source for serve directory/address)"
}

$cfg = Get-Content -LiteralPath $cfgPath -Raw | ConvertFrom-Json
if (-not $cfg.directory -or -not $cfg.bind -or -not $cfg.port) {
    throw "local CDN config incomplete: $cfgPath needs { directory, bind, port }"
}

$dir = [string]$cfg.directory
if (-not [System.IO.Path]::IsPathRooted($dir)) { $dir = Join-Path $projectRoot $dir }

[pscustomobject]@{
    ConfigPath = $cfgPath
    Directory  = $dir                # absolute publish root
    Bind       = [string]$cfg.bind
    Port       = [int]$cfg.port
}
