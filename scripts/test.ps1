[CmdletBinding()]
param(
    [ValidateSet('L1', 'L2', 'L3', 'All')]
    [string]$Lane = 'L1',

    [ValidateSet('PullRequest', 'Nightly', 'Release')]
    [string]$Profile = 'PullRequest',

    [string]$ProjectPath = '',
    [string]$ResultsDirectory = 'TestResults'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
} else {
    $ProjectPath = (Resolve-Path $ProjectPath).Path
}

if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $resultsPath = $ResultsDirectory
} else {
    $resultsPath = Join-Path $ProjectPath $ResultsDirectory
}

New-Item -ItemType Directory -Force -Path $resultsPath | Out-Null

$savedEnvironment = @{
    DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME
    DOTNET_CLI_TELEMETRY_OPTOUT = $env:DOTNET_CLI_TELEMETRY_OPTOUT
    DOTNET_PROCESSOR_COUNT = $env:DOTNET_PROCESSOR_COUNT
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
    LITETEST_ARTIFACTS = $env:LITETEST_ARTIFACTS
    LITETEST_RUN_ID = $env:LITETEST_RUN_ID
    M10_LONGRUN = $env:M10_LONGRUN
    NUGET_PACKAGES = $env:NUGET_PACKAGES
}

$runId = ('{0}-{1:yyyyMMdd-HHmmss}' -f $Profile.ToLowerInvariant(), [DateTime]::UtcNow)
$env:LITETEST_ARTIFACTS = Join-Path $resultsPath 'artifacts'
$env:LITETEST_RUN_ID = $runId
$env:DOTNET_CLI_HOME = Join-Path $ProjectPath '.dotnet-cli'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_PROCESSOR_COUNT = '4'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$userPackageCache = if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) { $null } else { Join-Path $env:USERPROFILE '.nuget\packages' }
if ($null -ne $userPackageCache -and (Test-Path $userPackageCache)) {
    $env:NUGET_PACKAGES = $userPackageCache
}

function Invoke-DotNetLane([string]$name, [string]$filter, [string]$hangTimeout) {
    Write-Host "`n=== $name | $Profile ===" -ForegroundColor Cyan
    $projects = @(Get-ChildItem -Path (Join-Path $ProjectPath 'Tests') -Recurse -Filter '*.Tests.csproj' -File | Sort-Object FullName)
    if ($projects.Count -eq 0) { throw 'No test projects were found under Tests/.' }

    foreach ($project in $projects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
        Write-Host "  -> restore $projectName (parallelism disabled)" -ForegroundColor DarkGray
        & dotnet restore $project.FullName --disable-parallel --ignore-failed-sources --verbosity minimal -m:1 -p:BuildInParallel=false -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) { throw "$name restore failed for $projectName with exit code $LASTEXITCODE." }
    }

    $solution = Join-Path $ProjectPath 'Tests/Tests.slnx'
    Write-Host '  -> build solution (single MSBuild node)' -ForegroundColor DarkGray
    & dotnet build $solution --configuration Release --no-restore --verbosity minimal -m:1 -p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) { throw "$name solution build failed with exit code $LASTEXITCODE." }

    $failedProjects = New-Object System.Collections.Generic.List[string]
    $total = 0
    foreach ($project in $projects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
        $resultName = "$($name.ToLowerInvariant())-$projectName-$runId.trx"
        $resultFile = Join-Path $resultsPath $resultName
        Write-Host "  -> $projectName" -ForegroundColor DarkGray

        $arguments = @(
            'test', $project.FullName,
            '--configuration', 'Release',
            '--no-build',
            '--no-restore',
            '--verbosity', 'minimal',
            '--results-directory', $resultsPath,
            '--logger', "trx;LogFileName=$resultName",
            '--blame-hang-timeout', $hangTimeout,
            '--filter', $filter
        )

        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            $failedProjects.Add($projectName)
            continue
        }

        if (Test-Path $resultFile) {
            [xml]$trx = Get-Content -Raw -LiteralPath $resultFile
            $counters = $trx.TestRun.ResultSummary.Counters
            if ($null -ne $counters) { $total += [int]$counters.total }
        }
    }

    if ($failedProjects.Count -gt 0) {
        throw "$name failed in: $($failedProjects -join ', '). Results: $resultsPath"
    }
    if ($total -le 0) { throw "$name selected zero tests. Check traits and filter: $filter" }
    Write-Host "  [PASS] $name completed $total tests." -ForegroundColor Green
}

function Invoke-L1 {
    # Untagged legacy tests deliberately remain in L1 during migration.
    $filter = 'Category!=Integration&Category!=EndToEnd&Category!=Performance&Category!=Quarantine&Duration!=LongRunning'
    Invoke-DotNetLane 'L1' $filter '5m'
}

function Invoke-L3 {
    $filter = '(Category=Integration|Category=EndToEnd)&Category!=Quarantine'
    $timeout = '5m'
    if ($Profile -eq 'PullRequest') {
        $filter += '&Duration!=LongRunning'
    } else {
        $env:M10_LONGRUN = '1'
        $timeout = '12m'
    }
    Invoke-DotNetLane 'L3' $filter $timeout
}

function Invoke-L2 {
    if ($env:OS -ne 'Windows_NT') {
        throw 'L2 requires Windows, a complete Unity project and a licensed Unity Editor.'
    }

    Write-Host "`n=== L2 | $Profile ===" -ForegroundColor Cyan
    $shell = (Get-Command powershell -ErrorAction Stop).Source
    & $shell -NoProfile -File (Join-Path $ProjectPath 'scripts/l2-unity-gate.ps1') -ProjectPath $ProjectPath
    if ($LASTEXITCODE -ne 0) {
        throw "L2 failed with exit code $LASTEXITCODE."
    }
}

try {
    switch ($Lane) {
        'L1' { Invoke-L1 }
        'L2' { Invoke-L2 }
        'L3' { Invoke-L3 }
        'All' {
            Invoke-L1
            Invoke-L3
            Invoke-L2
        }
    }
}
finally {
    foreach ($name in $savedEnvironment.Keys) {
        $value = $savedEnvironment[$name]
        if ($null -eq $value) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        } else {
            Set-Item "Env:$name" $value
        }
    }
}
