# 生成 LiteNet 协议代码（生成物入库：Assets/LiteNet/Proto/Generated/Battle.cs——运行期零工具链依赖）
# protoc 来源：nuget 包 Google.Protobuf.Tools 3.36.1（tools/windows_x64/protoc.exe；版本必须与运行时 Google.Protobuf 3.36.1 对齐）
# 用法：在仓库根执行  powershell -File scripts/codegen/gen-proto.ps1 -ProtocPath <protoc.exe 路径>
param(
    [Parameter(Mandatory = $true)]
    [string]$ProtocPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # scripts/codegen → 仓库根
$protoDir = Join-Path $root 'Assets/LiteNet/Proto'
$outDir = Join-Path $protoDir 'Generated'

& $ProtocPath --proto_path=$protoDir --csharp_out=$outDir (Join-Path $protoDir 'battle.proto')
if ($LASTEXITCODE -ne 0) { throw "protoc 生成失败（exit $LASTEXITCODE）" }
Write-Host "已生成: $outDir/Battle.cs"
