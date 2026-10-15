# ==============================================================================
# Local test chain - one command per server, keys kept machine-private.
#
# Per: local test manual Docs/ProjectNotes/07 (path C full chain) and the
#      deploy/.env gitignore slot (machine-private keys, never committed).
#
# Commands:
#   init   generate deploy/.env (5 keys + Mongo/bind defaults) if absent,
#          and write Assets/StreamingAssets/deploy.args (editor zero-config)
#   meta   build MetaServer, apply keys from deploy/.env into the bin copy
#          of appsettings.json, then start it in a new console window
#   room   start RoomServer in a new console window with keys from deploy/.env
#   up     meta + room
#   keys   print the current key summary (values masked)
#
# Keys live in deploy/.env (gitignored). Regenerating requires -Force on init.
# Full chain needs local MongoDB running (Mongo keys filled = persistence on).
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
# Usage: powershell -NoProfile -File scripts/local/local-chain.ps1 <command> [-Force]
# ==============================================================================
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Command = '',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = if ($PSScriptRoot) { (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$envFile = Join-Path $root 'deploy\.env'
$deployArgsFile = Join-Path $root 'Assets\StreamingAssets\deploy.args'

function New-Key32 {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    [Convert]::ToBase64String($bytes)
}

function Read-DotEnv([string]$path) {
    $map = @{}
    if (-not (Test-Path $path)) { return $map }
    foreach ($line in Get-Content $path) {
        $t = $line.Trim()
        if ($t -eq '' -or $t.StartsWith('#')) { continue }
        $i = $t.IndexOf('=')
        if ($i -le 0) { continue }
        $map[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
    }
    return $map
}

function Get-Config {
    if (-not (Test-Path $envFile)) {
        Write-Host "deploy/.env not found - run: local-chain.ps1 init" -ForegroundColor Red
        exit 1
    }
    return Read-DotEnv $envFile
}

function Write-DeployArgs([string]$envelopeKey, [string]$metaUrl) {
    $dir = Split-Path -Parent $deployArgsFile
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $text = "-envelope.key=$envelopeKey`n-meta.url=$metaUrl`n"
    [System.IO.File]::WriteAllText($deployArgsFile, $text)
    Write-Host "deploy.args written: $deployArgsFile (editor/exe zero-arg startup)" -ForegroundColor Green
}

function Invoke-Init {
    if ((Test-Path $envFile) -and -not $Force) {
        Write-Host "deploy/.env already exists (keep machine keys). Re-run with -Force to regenerate." -ForegroundColor Yellow
    }
    else {
        $lines = @(
            '# machine-private local chain keys - never commit',
            "ENVELOPE_PSK=$(New-Key32)",
            "AUTH_SIGNING_KEY=$(New-Key32)",
            "LOBBY_INSTANCE_KEY=$(New-Key32)",
            "LOBBY_TICKET_KEY=$(New-Key32)",
            "SETTLEMENT_KEY=$(New-Key32)",
            '# --- machine endpoints/paths (edit to taste) ---',
            'META_BIND=http://127.0.0.1:5000',
            'META_URL=http://127.0.0.1:5000',
            'MONGO_CONNECTION=mongodb://localhost:27017',
            'MONGO_DB=metadb',
            'ROOM_CONFIG=Config/roomserver.json',
            'ROOM_PORT=17777'
        )
        $dir = Split-Path -Parent $envFile
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        [System.IO.File]::WriteAllText($envFile, ($lines -join "`r`n") + "`r`n")
        Write-Host "deploy/.env written: $envFile" -ForegroundColor Green
    }

    $cfg = Read-DotEnv $envFile
    Write-DeployArgs $cfg['ENVELOPE_PSK'] $cfg['META_URL']
    Write-Host ''
    Write-Host 'Full chain ready-to-run: local-chain.ps1 up   (needs local MongoDB)' -ForegroundColor Cyan
    Write-Host 'Editor: just Play (deploy.args carries envelope + meta url), then log in on the entry page.' -ForegroundColor Cyan
}

function ConvertTo-MetaAppSettings([hashtable]$cfg, [string]$outPath) {
    # keys are NOT secrets-in-file here: this is the bin (output) copy that
    # MetaHost reads from AppContext.BaseDirectory. The tracked source file
    # (Server/MetaServer/appsettings.json) stays empty of values.
    $json = @"
{
  "Meta": {
    "BindAddress": "$($cfg['META_BIND'])",
    "MongoConnectionString": "$($cfg['MONGO_CONNECTION'])",
    "MongoDatabaseName": "$($cfg['MONGO_DB'])",
    "AuthSigningKeyBase64": "$($cfg['AUTH_SIGNING_KEY'])",
    "LobbyInstanceKeyBase64": "$($cfg['LOBBY_INSTANCE_KEY'])",
    "LobbyTicketKeyBase64": "$($cfg['LOBBY_TICKET_KEY'])",
    "LobbyTicketKeyId": "l1",
    "LobbyTicketAudience": "lobby",
    "SettlementInstanceKeyBase64": "$($cfg['SETTLEMENT_KEY'])",
    "EnableSampleEndpoints": false
  }
}
"@
    $dir = Split-Path -Parent $outPath
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($outPath, $json)
}

function Start-Console([string]$title, [string]$workingDir, [string]$inner) {
    # -EncodedCommand: base64 (UTF-16LE) the whole inner command - immune to
    # Start-Process argument quoting (no auto-quote on ArgumentList items).
    $cmd = "`$host.UI.RawUI.WindowTitle='$title'; Set-Location '$workingDir'; $inner"
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($cmd))
    Start-Process -FilePath 'powershell' -ArgumentList @('-NoExit', '-EncodedCommand', $encoded)
}

function Invoke-Meta {
    $cfg = Get-Config
    Write-Host 'building MetaServer...' -ForegroundColor Cyan
    dotnet build (Join-Path $root 'Server\MetaServer\MetaServer.csproj') --nologo -v q
    if ($LASTEXITCODE -ne 0) { Write-Host 'MetaServer build failed' -ForegroundColor Red; exit $LASTEXITCODE }
    $binSettings = Join-Path $root 'Server\MetaServer\bin\Debug\net8.0\appsettings.json'
    ConvertTo-MetaAppSettings $cfg $binSettings
    Write-Host 'keys applied to bin appsettings.json' -ForegroundColor Green
    if ($cfg['MONGO_CONNECTION'] -ne '') {
        Write-Host 'persistence ON - make sure local MongoDB is running' -ForegroundColor Yellow
    }
    else {
        Write-Host 'persistence OFF (Mongo empty in deploy/.env) - accounts/lobby run in-memory' -ForegroundColor Yellow
    }
    Start-Console 'MetaServer (local)' $root `
        "dotnet run --no-build --project Server\MetaServer -- --bind $($cfg['META_BIND'])"
    Write-Host 'MetaServer starting in new window (wait for the startup banner / /live)' -ForegroundColor Green
}

function Invoke-Room {
    $cfg = Get-Config
    $inner = "dotnet run --project Server\RoomServer -- --config $($cfg['ROOM_CONFIG']) --port $($cfg['ROOM_PORT'])" +
        " --envelope-key $($cfg['ENVELOPE_PSK'])" +
        " --ticket-key l1:$($cfg['LOBBY_TICKET_KEY'])" +
        " --lobby-instance-key $($cfg['LOBBY_INSTANCE_KEY'])" +
        " --settlement-instance-key $($cfg['SETTLEMENT_KEY'])"
    Start-Console 'RoomServer (local)' $root $inner
    Write-Host 'RoomServer starting in new window (banner shows template/lobby/settlement lines)' -ForegroundColor Green
    Write-Host 'NOTE: with a ticket key configured, F9 test-identity join is rejected - use the login page (full chain).' -ForegroundColor Yellow
}

function Invoke-Keys([hashtable]$cfg) {
    foreach ($name in @('ENVELOPE_PSK', 'AUTH_SIGNING_KEY', 'LOBBY_INSTANCE_KEY', 'LOBBY_TICKET_KEY', 'SETTLEMENT_KEY')) {
        $v = $cfg[$name]
        $masked = if ($v) { $v.Substring(0, 6) + '...' } else { '(missing)' }
        Write-Host ("{0,-28} {1}" -f $name, $masked)
    }
    foreach ($name in @('META_BIND', 'META_URL', 'MONGO_CONNECTION', 'MONGO_DB', 'ROOM_CONFIG', 'ROOM_PORT')) {
        Write-Host ("{0,-28} {1}" -f $name, $cfg[$name])
    }
}

switch ($Command) {
    'init' { Invoke-Init }
    'meta' { Invoke-Meta }
    'room' { Invoke-Room }
    'up'   { Invoke-Meta; Invoke-Room }
    'keys' { Invoke-Keys (Get-Config) }
    default {
        Write-Host 'Usage: local-chain.ps1 <init|meta|room|up|keys> [-Force]' -ForegroundColor Cyan
        Write-Host '  init  write deploy/.env (5 keys) + editor deploy.args' -ForegroundColor Gray
        Write-Host '  meta  start MetaServer (keys from deploy/.env)' -ForegroundColor Gray
        Write-Host '  room  start RoomServer (keys from deploy/.env)' -ForegroundColor Gray
        Write-Host '  up    meta + room' -ForegroundColor Gray
        Write-Host '  keys  masked key summary' -ForegroundColor Gray
        exit 1
    }
}
