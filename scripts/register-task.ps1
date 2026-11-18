<#
.SYNOPSIS
  Registers the "DiskSentinel" scheduled task: start elevated at logon, no time limit.
  Must be run as administrator. Used by install.ps1 and by the Setup installer.

.PARAMETER ExePath
  Full path to DiskSentinel.exe.

.PARAMETER User
  The account to run as. Defaults to the interactively signed-in user (which is the right
  account even when an installer was elevated with different administrator credentials).
#>
param(
    [Parameter(Mandatory)][string]$ExePath,
    [string]$User
)

$ErrorActionPreference = 'Stop'

if (-not $User) { $User = (Get-CimInstance Win32_ComputerSystem).UserName }
if (-not $User) { $User = "$env:USERDOMAIN\$env:USERNAME" }

$action = New-ScheduledTaskAction -Execute $ExePath
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $User
$principal = New-ScheduledTaskPrincipal -UserId $User -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
Register-ScheduledTask -TaskName 'DiskSentinel' -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "Registered the DiskSentinel startup task for $User."
