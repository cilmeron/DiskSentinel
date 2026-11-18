<#
.SYNOPSIS
  Builds the Setup installer: installer\Output\DiskSentinel-Setup-<version>.exe

.DESCRIPTION
  1. Publishes DiskSentinel self-contained (no .NET runtime needed on the target PC).
  2. Collects smartctl.exe and its licence from an installed smartmontools.
  3. Compiles installer\DiskSentinel.iss with Inno Setup 6.

  Needs: .NET SDK, smartmontools (winget install smartmontools.smartmontools),
         Inno Setup 6 (winget install JRSoftware.InnoSetup).
#>
param([string]$Version)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$staging = Join-Path $PSScriptRoot 'staging'

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'DiskSentinel.csproj'))).Project.PropertyGroup.Version | Select-Object -First 1
}
Write-Host "== Building DiskSentinel $Version"

# --- locate tools ---
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup' }

$smartDir = @(
    "$env:ProgramFiles\smartmontools",
    "${env:ProgramFiles(x86)}\smartmontools"
) | Where-Object { Test-Path "$_\bin\smartctl.exe" } | Select-Object -First 1
if (-not $smartDir) { throw 'smartmontools not found. Install it with: winget install smartmontools.smartmontools' }

# --- stage files ---
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$staging\smartctl" | Out-Null

dotnet publish (Join-Path $root 'DiskSentinel.csproj') -c Release -r win-x64 --self-contained true `
    -p:DebugType=none -p:SatelliteResourceLanguages=en -o "$staging\app"
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item "$smartDir\bin\smartctl.exe" "$staging\smartctl\"
Copy-Item "$smartDir\doc\COPYING.txt" "$staging\smartctl\COPYING.txt"
Copy-Item "$smartDir\doc\README.txt" "$staging\smartctl\README.txt"

$smartVersion = (& "$smartDir\bin\smartctl.exe" --version | Select-Object -First 1)
@"
DiskSentinel is released under the MIT License:

$(Get-Content (Join-Path $root 'LICENSE') -Raw)
------------------------------------------------------------------------------
This installer also includes smartctl, part of smartmontools
($smartVersion), which is licensed under the GNU General Public License,
version 2 or later. smartctl is run as a separate program; DiskSentinel does not
link against it. smartmontools source code and downloads: https://www.smartmontools.org/
The full GPL text follows.
------------------------------------------------------------------------------

$(Get-Content "$smartDir\doc\COPYING.txt" -Raw)
"@ | Set-Content "$staging\LICENSES.txt" -Encoding utf8

# --- compile the installer ---
Push-Location $PSScriptRoot
try {
    & $iscc "/DAppVersion=$Version" "/DStagingDir=$staging" 'DiskSentinel.iss'
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compile failed' }
}
finally { Pop-Location }

$out = Join-Path $PSScriptRoot "Output\DiskSentinel-Setup-$Version.exe"
Write-Host ("Built {0} ({1:N1} MB)" -f $out, ((Get-Item $out).Length / 1MB))
