# Generate LiteNet protocol code from battle.proto. The generated file is
# checked in (Assets/LiteNet/Proto/Generated/Battle.cs) so the runtime keeps
# zero toolchain dependency.
#
# protoc source: nuget package Google.Protobuf.Tools (tools/windows_x64/protoc.exe);
# the protoc version MUST match the runtime Google.Protobuf package version.
#
# Toolbox entry:  pwsh scripts/codegen/toolbox.ps1 proto -ProtocPath <protoc.exe>
# Direct entry:   pwsh scripts/codegen/gen-proto.ps1 -ProtocPath <protoc.exe>
# Output:         Assets/LiteNet/Proto/Generated/Battle.cs (generated file, DO NOT EDIT)
# Rerun when:     battle.proto changed (then rerun build-hash: Proto/ is in its source set)
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
[CmdletBinding()]
param(
    [string]$ProtocPath = ''
)

. (Join-Path $PSScriptRoot '_common.ps1')

function Invoke-ProtoGeneration {
    $root = Get-CodegenRepoRoot
    $protoDir = Join-Path $root 'Assets/LiteNet/Proto'
    $outDir = Join-Path $protoDir 'Generated'

    if ([string]::IsNullOrWhiteSpace($ProtocPath)) {
        throw 'usage: gen-proto.ps1 -ProtocPath <protoc.exe> (nuget Google.Protobuf.Tools, tools/windows_x64/protoc.exe)'
    }
    if (-not (Test-Path (Join-Path $protoDir 'battle.proto'))) {
        throw "battle.proto not found: $protoDir"
    }
    if (-not (Test-Path $ProtocPath) -and -not (Get-Command $ProtocPath -ErrorAction SilentlyContinue)) {
        throw "protoc not found: $ProtocPath (nuget Google.Protobuf.Tools, tools/windows_x64/protoc.exe)"
    }

    & $ProtocPath --proto_path=$protoDir --csharp_out=$outDir (Join-Path $protoDir 'battle.proto')
    if ($LASTEXITCODE -ne 0) { throw "protoc generation failed (exit $LASTEXITCODE)" }
    Write-Tool 'proto' "generated: Assets/LiteNet/Proto/Generated/Battle.cs"
}

try { Invoke-ProtoGeneration; exit 0 }
catch { Write-ToolFail 'proto' $_.Exception.Message; exit 1 }
