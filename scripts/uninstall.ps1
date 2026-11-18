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
Remove-Item (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\DiskSentinel.lnk') -ErrorAction SilentlyContinue
# History is kept in %ProgramData%\DiskSentinel; delete that folder to remove it too.
Write-Host 'DiskSentinel removed.'
