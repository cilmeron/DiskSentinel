<#
.SYNOPSIS
  Builds DiskSentinel, installs it to Program Files and starts it automatically at logon.

.PARAMETER RemoveHardDiskSentinel
  Also uninstalls Hard Disk Sentinel, but only after DiskSentinel has proven it can read
  every physical drive in this PC.

.PARAMETER LogPath
  Optional file to write a transcript to.

Requires: .NET 10 SDK (to build) and smartmontools (installed via winget if missing).
Autostart uses a scheduled task that runs elevated at logon, so there is no UAC prompt
on every login (reading SATA drives needs administrator rights).
#>
param(
    [switch]$RemoveHardDiskSentinel,
    [string]$LogPath,
    [string]$ScreenshotDir   # dev option: save a PNG of each window tab after installing
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($RemoveHardDiskSentinel) { $argList += '-RemoveHardDiskSentinel' }
    if ($LogPath) { $argList += @('-LogPath', "`"$LogPath`"") }
    if ($ScreenshotDir) { $argList += @('-ScreenshotDir', "`"$ScreenshotDir`"") }
    Start-Process powershell -Verb RunAs -ArgumentList $argList -Wait
    exit
}

if ($LogPath) { Start-Transcript -Path $LogPath -Force | Out-Null }

try {
    $projectDir = Split-Path $PSScriptRoot -Parent
    $dest = Join-Path $env:ProgramFiles 'DiskSentinel'
    $exe = Join-Path $dest 'DiskSentinel.exe'
    $user = "$env:USERDOMAIN\$env:USERNAME"

    Write-Host '== 1/5 smartmontools'
    $smartctl = (Get-Command smartctl -ErrorAction SilentlyContinue).Source
    if (-not $smartctl -and -not (Test-Path "$env:ProgramFiles\smartmontools\bin\smartctl.exe")) {
        winget install --id smartmontools.smartmontools -e --accept-package-agreements --accept-source-agreements
    }

    Write-Host '== 2/5 build and install'
    Get-Process DiskSentinel -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    if (Test-Path "$projectDir\DiskSentinel.dll") {
        # Release zip layout: prebuilt files sit next to the scripts folder (needs the .NET 10 Desktop Runtime).
        New-Item -ItemType Directory -Force $dest | Out-Null
        Get-ChildItem $projectDir -Exclude scripts, README.md, LICENSE | Copy-Item -Destination $dest -Recurse -Force
    } else {
        dotnet publish "$projectDir\DiskSentinel.csproj" -c Release -o $dest --self-contained false
        if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    }

    Write-Host '== 3/5 smoke test (can every drive be read?)'
    $dump = Join-Path $env:TEMP 'disksentinel-smoke.txt'
    Remove-Item $dump -ErrorAction SilentlyContinue
    Start-Process $exe -ArgumentList '--dump', "`"$dump`"" -Wait
    $expected = @(Get-PhysicalDisk).Count
    $readable = if (Test-Path $dump) { @(Select-String -Path $dump -Pattern '^Temperature\s+: \d').Count } else { 0 }
    Write-Host "Readable drives: $readable of $expected"
    if (Test-Path $dump) { Get-Content $dump | Write-Host }
    $allReadable = ($readable -ge $expected -and $expected -gt 0)

    Write-Host '== 4/5 autostart task'
    $action = New-ScheduledTaskAction -Execute $exe
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
    Register-ScheduledTask -TaskName 'DiskSentinel' -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Force | Out-Null

    # Start Menu shortcut (launching it while running just brings the window forward)
    $lnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\DiskSentinel.lnk'
    $sc = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
    $sc.TargetPath = $exe
    $sc.Arguments = '--show'
    $sc.WorkingDirectory = $dest
    $sc.Save()

    if ($ScreenshotDir) {
        Start-Process $exe -ArgumentList '--screenshot', "`"$ScreenshotDir`"" -Wait
    }

    Write-Host '== 5/5 Hard Disk Sentinel'
    if ($RemoveHardDiskSentinel) {
        if (-not $allReadable) {
            Write-Warning 'Not every drive was readable, so Hard Disk Sentinel was NOT removed.'
        } else {
            Get-Process | Where-Object { $_.Name -like 'HDSentinel*' } | Stop-Process -Force -ErrorAction SilentlyContinue
            Get-ScheduledTask | Where-Object { $_.TaskName -like '*Hard Disk Sentinel*' } |
                Unregister-ScheduledTask -Confirm:$false
            $uninst = 'C:\Program Files (x86)\Hard Disk Sentinel\unins000.exe'
            if (Test-Path $uninst) {
                Start-Process $uninst -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
                Write-Host 'Hard Disk Sentinel uninstalled.'
            } else {
                Write-Host 'Hard Disk Sentinel uninstaller not found (already removed?).'
            }
        }
    } else {
        Write-Host 'Skipped (pass -RemoveHardDiskSentinel to remove it).'
    }

    Start-ScheduledTask -TaskName 'DiskSentinel'
    Write-Host 'Done. DiskSentinel is running and will start automatically at logon.'
}
finally {
    if ($LogPath) { Stop-Transcript | Out-Null }
}
