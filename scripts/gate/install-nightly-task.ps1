# Register (or repair) the LiteGame-Nightly-Gate scheduled task.
#
# The task registration is an external surface: moving scripts does not update it.
# Values come from scripts/gate/toolchain.json -> surfaces[id=nightly-task].
# The task must run under pwsh (PowerShell 7); powershell.exe 5.1 misparses
# non-BOM UTF-8 CJK scripts on zh-CN systems.
#
# Usage:
#   pwsh scripts/gate/install-nightly-task.ps1 -WhatIf          # print action, no change
#   pwsh scripts/gate/install-nightly-task.ps1 -Force          # replace existing task
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ProjectPath = '',
    [string]$TaskName = '',
    [string]$Script = '',
    [string]$Time = '03:00',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $ProjectPath = (Resolve-Path (Join-Path $here '..\..')).Path
}

$manifestPath = Join-Path $ProjectPath 'scripts\gate\toolchain.json'
if (-not (Test-Path $manifestPath)) { throw "toolchain manifest missing: $manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$surface = @($manifest.surfaces | Where-Object { $_.id -eq 'nightly-task' })[0]
if ($null -eq $surface) { throw "manifest has no nightly-task surface" }

if ([string]::IsNullOrWhiteSpace($TaskName)) { $TaskName = $surface.name }
if ([string]::IsNullOrWhiteSpace($Script)) { $Script = $surface.script }
if ([string]::IsNullOrWhiteSpace($TaskName)) { throw "nightly-task surface has no name" }
if ([string]::IsNullOrWhiteSpace($Script)) { throw "nightly-task surface has no script" }

$scriptPath = Join-Path $ProjectPath $Script
if (-not (Test-Path $scriptPath)) { throw "nightly script missing: $scriptPath" }

$pwsh = Get-Command 'pwsh' -ErrorAction Stop
$user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $scriptPath
$action = New-ScheduledTaskAction -Execute $pwsh.Source -Argument $arguments -WorkingDirectory $ProjectPath
$trigger = New-ScheduledTaskTrigger -Daily -At $Time
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)

Write-Host "task:     $TaskName"
Write-Host "runner:   $($pwsh.Source)"
Write-Host "script:   $scriptPath"
Write-Host "trigger:  daily $Time"
Write-Host "user:     $user (Interactive/Limited)"

if (-not $PSCmdlet.ShouldProcess($TaskName, 'Register scheduled task')) {
    Write-Host 'WhatIf: task registration skipped'
    exit 0
}

$register = @{
    TaskName    = $TaskName
    Action      = $action
    Trigger     = $trigger
    Principal   = $principal
    Settings    = $settings
    Description = 'LiteGame nightly gate (L1 + L2)'
}
if ($Force) { $register.Force = $true }
Register-ScheduledTask @register | Out-Null

$info = Get-ScheduledTaskInfo -TaskName $TaskName
Write-Host "registered: $TaskName; next run: $($info.NextRunTime)"
exit 0
