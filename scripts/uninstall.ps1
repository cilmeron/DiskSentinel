<#
.SYNOPSIS
  Removes DiskSentinel: stops it, deletes the autostart task and the install folder.
  Leaves smartmontools installed.
#>
$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell -Verb RunAs -Wait -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`""
    exit
}

Get-Process DiskSentinel -ErrorAction SilentlyContinue | Stop-Process -Force
Unregister-ScheduledTask -TaskName 'DiskSentinel' -Confirm:$false -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:ProgramFiles 'DiskSentinel') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'DiskSentinel removed.'
