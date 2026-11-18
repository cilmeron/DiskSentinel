# DiskSentinel

A small, free Windows tray app that shows the temperature of each of your drives as a
tray icon, inspired by Hard Disk Sentinel's tray display.

- One tray icon per drive showing its temperature (yellow, orange at 55 °C, red at 65 °C)
- Hover for health, click for full details: health %, wear, power-on time, lifetime
  writes, power cycles, reallocated/pending sectors, media errors
- Works with SATA and NVMe drives, including SATA drives behind a RAID/RST controller
- Balloon alerts for critical temperature, low health or a failed S.M.A.R.T. check
- Starts automatically at logon via an elevated scheduled task (no UAC prompt each login)

Drive data comes from [smartmontools](https://www.smartmontools.org/) (`smartctl`).

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- smartmontools (the installer adds it with winget if missing)

## Install

From a PowerShell prompt in the repo:

```powershell
.\scripts\install.ps1
```

It builds the app, installs it to `C:\Program Files\DiskSentinel`, registers the
autostart task and starts it. It asks for administrator rights because reading SATA
drives requires them.

To replace Hard Disk Sentinel at the same time:

```powershell
.\scripts\install.ps1 -RemoveHardDiskSentinel
```

Hard Disk Sentinel is only removed if DiskSentinel was able to read every drive in the PC.

To uninstall: `.\scripts\uninstall.ps1`

## Run from source

```powershell
dotnet run -c Release
```

The app relaunches itself elevated. Pass `--no-elevate` to skip that (SATA drives will
then be unreadable, but NVMe drives usually still work).

Diagnostics:

```powershell
DiskSentinel.exe --dump out.txt              # write drive details to a file
DiskSentinel.exe --render-icon 43 icon.png   # preview a tray icon
```

## Configuration

Thresholds and refresh interval are constants in [`src/Settings.cs`](src/Settings.cs).

## How health is calculated

Health starts at 100% and is capped by the drive's remaining-life value (NVMe
`percentage_used`, or the vendor life attributes on SATA SSDs). It is reduced for
reallocated/pending sectors, media errors, low spare capacity, critical temperature,
and capped low if the S.M.A.R.T. self-assessment fails.
This is a heuristic, not a guarantee. Keep backups.

## License

MIT
