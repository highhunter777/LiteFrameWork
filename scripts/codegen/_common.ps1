# Shared single source for the generator toolbox (scripts/codegen/*).
#
# Every generator here follows one form (see the generator toolbox design doc):
#   - repo root is derived from the script's own location, never from CWD;
#   - output goes through Write-Tool* with a `[codegen/<tool>]` prefix;
#   - expected failures surface as ONE clean `[codegen/<tool>] FAILED: ...`
#     line + exit code 1 -- never a raw stack trace, never a silent no-op.
#
# Dot-source from each tool:  . (Join-Path $PSScriptRoot '_common.ps1')
#
# NOTE: keep this file ASCII-only (PS 5.1 GBK parsing of non-BOM UTF-8).

function Get-CodegenRepoRoot {
    # scripts/codegen -> repo root (scripts/README convention: derivable from any CWD).
    return Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

function Write-Tool {
    param([Parameter(Mandatory = $true)][string]$Tool, [Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[codegen/$Tool] $Message"
}

function Write-ToolWarn {
    param([Parameter(Mandatory = $true)][string]$Tool, [Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[codegen/$Tool] WARN: $Message" -ForegroundColor Yellow
}

function Write-ToolFail {
    param([Parameter(Mandatory = $true)][string]$Tool, [Parameter(Mandatory = $true)][string]$Message)
    Write-Host "[codegen/$Tool] FAILED: $Message" -ForegroundColor Red
}

function Get-FileSha256Base64 {
    # Content hash used by publish steps to skip byte-identical files.
    param([Parameter(Mandatory = $true)][string]$Path)
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToBase64String($hasher.ComputeHash([System.IO.File]::ReadAllBytes($Path)))
    } finally {
        $hasher.Dispose()
    }
}
