# Generator toolbox - the unified entry over scripts/codegen/*.
#
# Usage:
#   pwsh scripts/codegen/toolbox.ps1                    # list tools
#   pwsh scripts/codegen/toolbox.ps1 <tool> [args...]   # run one tool
#
# Tools (form/operation details: the generator toolbox design doc, Docs design
# center; per-tool why/notes stay in each script's header):
#   build-hash    buildHash generator; --check = build gate (Join handshake red line)
#   proto         battle.proto -> Assets/LiteNet/Proto/Generated/Battle.cs
#   luban         table chain regen (C#/binary/lua/LuaKeys); alias of Luban/gen.bat
#   sync-code     [StateLayer] fields -> EntitySlot.Sync.g.cs (checksum plumbing)
#   refresh-hash  refresh orchestration: gen -> RoomServer build -> Unity recompile -> reconcile
#
# Direct per-script invocation stays valid (CI/gate callers keep their exact
# commands); this entry only forwards arguments and the child exit code.
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).

[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Tool = '',
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$ToolArgs = @()
)

. (Join-Path $PSScriptRoot '_common.ps1')

# scripts/README convention: script paths resolve relative to this directory,
# so the toolbox works from any CWD.
$Tools = [ordered]@{
    'build-hash'   = @{ Engine = 'python'; Script = 'gen-build-hash.py'; Usage = '[--check]';                    Summary = 'buildHash generator; --check is the build gate (Join handshake red line)' }
    'proto'        = @{ Engine = 'pwsh';   Script = 'gen-proto.ps1';     Usage = '-ProtocPath <protoc.exe>';    Summary = 'battle.proto -> Generated/Battle.cs (protoc must match runtime version)' }
    'luban'        = @{ Engine = 'pwsh';   Script = 'gen-luban.ps1';     Usage = '[-PythonPath <python>]';      Summary = 'table chain regen (C#/binary/lua/LuaKeys); alias of Luban/gen.bat' }
    'sync-code'    = @{ Engine = 'pwsh';   Script = 'gen-sync-code.ps1'; Usage = '';                            Summary = '[StateLayer] fields -> EntitySlot.Sync.g.cs (checksum plumbing)' }
    'refresh-hash' = @{ Engine = 'pwsh';   Script = 'refresh-hash.ps1';  Usage = '[-SkipServerBuild] [-SkipUnityRecompile]'; Summary = 'refresh orchestration: gen -> RoomServer build -> Unity recompile -> reconcile' }
}

function Show-ToolboxIndex {
    Write-Host 'Generator toolbox (scripts/codegen/) - unified entry:'
    foreach ($name in $Tools.Keys) {
        $entry = $Tools[$name]
        $tail = if ($entry.Usage) { " $($entry.Usage)" } else { '' }
        Write-Host ("  {0,-13} toolbox.ps1 {1}{2}" -f $name, $name, $tail)
        Write-Host ("  {0,-13} {1}" -f '', $entry.Summary)
    }
    Write-Host 'Direct per-script invocation stays valid; this entry only forwards args.'
}

if ([string]::IsNullOrWhiteSpace($Tool)) {
    Show-ToolboxIndex
    exit 0
}

$entry = $Tools[$Tool]
if ($null -eq $entry) {
    Write-ToolFail 'toolbox' "unknown tool '$Tool' - known tools: $($Tools.Keys -join ', ')"
    Show-ToolboxIndex
    exit 1
}

$scriptPath = Join-Path $PSScriptRoot $entry.Script
if (-not (Test-Path $scriptPath)) {
    Write-ToolFail 'toolbox' "tool script missing: $($entry.Script)"
    exit 1
}

if ($entry.Engine -eq 'python') {
    & python $scriptPath @ToolArgs
    exit $LASTEXITCODE
}

& pwsh -NoProfile -File $scriptPath @ToolArgs
exit $LASTEXITCODE
