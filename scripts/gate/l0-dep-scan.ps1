# ─────────────────────────────────────────────────────────────────────────────
# L0 dependency & secret scan
#
# Per: Client Framework Master Design 16, row 1 - L0 must intercept personal
#      absolute package paths, floating git deps, missing licenses and secrets.
# Checks:
#   1) manifest/lock contain zero personal absolute file: paths
#   2) git deps must be pinned to a commit (.git#<40-hex>); no floating branch
#   3) vendored packages must keep their LICENSE file
#   4) secret patterns in tracked text files (private keys, AKIA, ghp_, hardcoded creds)
# NOTE: keep this file ASCII-only. Windows PowerShell 5.1 parses non-BOM
#       UTF-8 as GBK on zh-CN systems and breaks on CJK comments.
# Exit code: 0 = pass, 1 = violations. Wire into CI (L0 must-run on PR).
# Usage: powershell -NoProfile -File scripts/gate/l0-dep-scan.ps1
# ─────────────────────────────────────────────────────────────────────────────
[CmdletBinding()]
param(
    [string]$ProjectPath = ''
)

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..\..')).Path
}

$violations = New-Object System.Collections.Generic.List[string]
Push-Location $ProjectPath
try {
    # -- 1+2) manifest and lock: personal absolute paths / floating git --------
    foreach ($f in @('Packages/manifest.json', 'Packages/packages-lock.json')) {
        if (-not (Test-Path $f)) { $violations.Add("$f missing (dependency declarations must be committed)"); continue }
        $lines = Get-Content $f
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'file:[A-Za-z]:') {
                $violations.Add("$f line $($i+1): personal absolute path dep - $($lines[$i].Trim())")
            }
            if ($lines[$i] -match '\.git"' -and $lines[$i] -notmatch '\.git#[0-9a-f]{40}') {
                $violations.Add("$f line $($i+1): floating git dep (pin to commit: .git#<40-hex>) - $($lines[$i].Trim())")
            }
        }
    }

    # -- 3+5) vendored packages: license present (only for packages actually
    #        tracked in the repo; gitignored local tooling such as MCPForUnity
    #        and com.unity.pipeline is not redistributed, so no license needed)
    $embeddedDirs = Get-ChildItem 'Packages' -Directory -ErrorAction SilentlyContinue
    foreach ($dir in $embeddedDirs) {
        $pkgJson = Join-Path $dir.FullName 'package.json'
        if (-not (Test-Path $pkgJson)) { continue }  # not a UPM package dir
        $trackedCount = 0
        $trackedCount = (& git ls-files -- "Packages/$($dir.Name)/package.json") 2>$null | Measure-Object | Select-Object -ExpandProperty Count
        if (-not $trackedCount) { continue }  # local tooling, not shipped
        $licenseFile = Get-ChildItem $dir.FullName -File | Where-Object { $_.Name -match '^(LICENSE|License)' }
        if (-not $licenseFile) { $violations.Add("vendored package $($dir.Name) has no LICENSE file (redistribution requires it)") }
    }

    # -- 4) secret pattern scan over git-tracked text files ---------------------
    $tracked = (& git ls-files) 2>$null
    if ($LASTEXITCODE -eq 0 -and $tracked) {
        $textTargets = $tracked | Where-Object { $_ -match '\.(json|ps1|psm1|py|cs|md|txt|yml|yaml|xml|config|bat|sh)$' -and (Test-Path $_) }
        $secretPatterns = @(
            @{ Pattern = '-----BEGIN (RSA |EC |DSA |OPENSSH )?PRIVATE KEY-----'; Name = 'private key material' },
            @{ Pattern = 'AKIA[0-9A-Z]{16}';                                     Name = 'AWS access key' },
            @{ Pattern = 'ghp_[A-Za-z0-9]{36}';                                   Name = 'GitHub token' },
            @{ Pattern = '(?i)(api[_-]?key|secret|password)\s*[:=]\s*["''][A-Za-z0-9+/=_.-]{24,}["'']'; Name = 'hardcoded credential' }
        )
        $secretExcludes = @('l0-dep-scan.ps1', 'JoinAdmissionGateTests.cs')  # self (contains pattern text); JoinAdmissionGateTests.cs = fake fixture token for the no-echo assertion, not a credential
        foreach ($file in $textTargets) {
            if ($secretExcludes -contains (Split-Path $file -Leaf)) { continue }
            $content = $null
            try { $content = Get-Content $file -Raw -ErrorAction Stop } catch { continue }
            if (-not $content) { continue }
            foreach ($sp in $secretPatterns) {
                if ($content -match $sp.Pattern) {
                    $violations.Add("possible secret ($($sp.Name)): $file - review manually; if false positive register it in l0-dep-scan.ps1 secretExcludes")
                }
            }
        }
    }
    else {
        Write-Warning 'git unavailable or not a repo - secret scan skipped (CI always runs inside a repo)'
    }
}
finally { Pop-Location }

Write-Host "L0 dependency & secret scan | project: $ProjectPath" -ForegroundColor White
if ($violations.Count -eq 0) {
    Write-Host '  [PASS] deps reproducible, zero floating deps, vendored licenses kept, no secret hits' -ForegroundColor Green
    exit 0
}
foreach ($v in $violations) { Write-Host "  [FAIL] $v" -ForegroundColor Red }
Write-Host "L0 scan FAILED: $($violations.Count) violation(s)" -ForegroundColor Red
exit 1
