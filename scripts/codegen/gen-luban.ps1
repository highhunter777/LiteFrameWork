# Regenerate the Luban table chain (C#/binary/lua/LuaKeys) and publish only
# changed files. Luban clears its output directory, so generation runs OUTSIDE
# Assets (TestResults staging) and publish copies only declared file types,
# preserving existing asmdef/meta files under Assets.
#
# Toolbox entry:  pwsh scripts/codegen/toolbox.ps1 luban
# Direct entry:   pwsh scripts/codegen/gen-luban.ps1 [-PythonPath <python>]
# Shim alias:     Luban/gen.bat (delegates here)
# Outputs:        Assets/GameData/Generated/*.cs, Assets/GameData/Config/*.bytes,
#                 Assets/LiteGame/Lua/Cfg/*.lua, Assets/LiteGame/LuaBridge/Generated/LuaKeys.g.cs
# Rerun when:     table source (Luban/Data/*.xlsx) or schema changed - then rerun
#                 build-hash (table data is in its source set); Unity import is separate.
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).
[CmdletBinding()]
param(
    [string]$PythonPath = 'python'
)

. (Join-Path $PSScriptRoot '_common.ps1')

function Invoke-LubanGeneration {
    $repoRoot = Get-CodegenRepoRoot
    $lubanRoot = Join-Path $repoRoot 'Luban'
    $lubanExe = Join-Path $lubanRoot 'LubanGenerater\Luban\Luban.exe'
    $configPath = Join-Path $lubanRoot 'luban.conf'
    $stageRoot = Join-Path $repoRoot ('TestResults\luban-generation\' + [guid]::NewGuid().ToString('N'))
    $stageCode = Join-Path $stageRoot 'code'
    $stageBin = Join-Path $stageRoot 'bin'
    $stageLua = Join-Path $stageRoot 'lua'
    $stageLuaKeys = Join-Path $stageRoot 'lua-keys'

    if (-not (Test-Path $lubanExe)) { throw "Luban.exe not found: $lubanExe" }

    # Luban clears its output directory. Generate outside Assets so existing asmdef/meta
    # and unrelated generated assets remain intact; publish only declared file types.
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    & $lubanExe -t client -c cs-bin -d bin --conf $configPath `
        -x "outputCodeDir=$stageCode" -x "outputDataDir=$stageBin"
    if ($LASTEXITCODE -ne 0) { throw "Luban C#/binary generation failed: $LASTEXITCODE" }
    & $lubanExe -t client -d lua --conf $configPath -x "outputDataDir=$stageLua"
    if ($LASTEXITCODE -ne 0) { throw "Luban Lua generation failed: $LASTEXITCODE" }
    & $PythonPath (Join-Path $lubanRoot 'gen_lua_keys.py') --output (Join-Path $stageLuaKeys 'LuaKeys.g.cs')
    if ($LASTEXITCODE -ne 0) { throw "Lua key generation failed: $LASTEXITCODE" }

    function Publish-GeneratedFiles([string]$sourceDir, [string]$targetDir, [string]$extension) {
        foreach ($source in Get-ChildItem -LiteralPath $sourceDir -Recurse -File -Filter "*$extension") {
            $relative = $source.FullName.Substring($sourceDir.Length).TrimStart('\', '/')
            $target = Join-Path $targetDir $relative
            # Preserve an existing text file's line endings; new text uses LF. Binary stays exact.
            if ($extension -ne '.bytes') {
                $text = [System.IO.File]::ReadAllText($source.FullName).Replace("`r`n", "`n").Replace("`r", "`n")
                if ((Test-Path -LiteralPath $target) -and
                    [System.IO.File]::ReadAllText($target).Contains("`r`n")) {
                    $text = $text.Replace("`n", "`r`n")
                }
                [System.IO.File]::WriteAllText($source.FullName, $text, (New-Object System.Text.UTF8Encoding($false)))
            }
            if ((Test-Path -LiteralPath $target) -and
                ((Get-FileSha256Base64 $source.FullName) -eq (Get-FileSha256Base64 $target))) {
                continue
            }
            New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $source.FullName -Destination $target
            Write-Tool 'luban' "publish: $relative"
        }
    }

    Publish-GeneratedFiles $stageCode (Join-Path $repoRoot 'Assets\GameData\Generated') '.cs'
    Publish-GeneratedFiles $stageBin (Join-Path $repoRoot 'Assets\GameData\Config') '.bytes'
    Publish-GeneratedFiles $stageLua (Join-Path $repoRoot 'Assets\LiteGame\Lua\Cfg') '.lua'
    Publish-GeneratedFiles $stageLuaKeys (Join-Path $repoRoot 'Assets\LiteGame\LuaBridge\Generated') '.cs'
    Write-Tool 'luban' 'completed; Unity import and validation are separate.'
}

try { Invoke-LubanGeneration; exit 0 }
catch { Write-ToolFail 'luban' $_.Exception.Message; exit 1 }
