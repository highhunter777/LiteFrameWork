[CmdletBinding()]
param(
    [string]$PythonPath = 'python'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$lubanRoot = Join-Path $repoRoot 'Luban'
$lubanExe = Join-Path $lubanRoot 'LubanGenerater\Luban\Luban.exe'
$configPath = Join-Path $lubanRoot 'luban.conf'
$stageRoot = Join-Path $repoRoot ('TestResults\luban-generation\' + [guid]::NewGuid().ToString('N'))
$stageCode = Join-Path $stageRoot 'code'
$stageBin = Join-Path $stageRoot 'bin'
$stageLua = Join-Path $stageRoot 'lua'
$stageLuaKeys = Join-Path $stageRoot 'lua-keys'

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

function Get-GeneratedFileHash([string]$path) {
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToBase64String($hasher.ComputeHash([System.IO.File]::ReadAllBytes($path)))
    } finally {
        $hasher.Dispose()
    }
}

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
            ((Get-GeneratedFileHash $source.FullName) -eq (Get-GeneratedFileHash $target))) {
            continue
        }
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $source.FullName -Destination $target
        Write-Host "Generated: $relative"
    }
}

Publish-GeneratedFiles $stageCode (Join-Path $repoRoot 'Assets\GameData\Generated') '.cs'
Publish-GeneratedFiles $stageBin (Join-Path $repoRoot 'Assets\GameData\Config') '.bytes'
Publish-GeneratedFiles $stageLua (Join-Path $repoRoot 'Assets\LiteGame\Lua\Cfg') '.lua'
Publish-GeneratedFiles $stageLuaKeys (Join-Path $repoRoot 'Assets\LiteGame\LuaBridge\Generated') '.cs'
Write-Host 'Luban generation completed; Unity import and validation are separate.'
