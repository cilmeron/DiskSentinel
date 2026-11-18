# DiskSentinel

A small, free Windows tray app that shows the temperature of each of your drives as a
tray icon, inspired by Hard Disk Sentinel's tray display.

- One tray icon per drive showing its temperature (yellow, orange at 55 °C, red at 65 °C)
- Closing the window only hides it; the app keeps running in the tray until you choose
  **Exit** from a tray icon's right-click menu. Left-click a tray icon to reopen the window.
- Main window with a drive list, volume free space and tabs:
  - **Overview**: health bar, status, power-on time, estimated remaining lifetime (years,
    months, days), lifetime writes, power cycles, and a health history chart
  - **Temperature**: history chart (1 hour to all time) with min/avg/max and a hover read-out
  - **S.M.A.R.T.**: the full attribute table (ID, current, worst, threshold, data, status)
  - **Information**: model, serial, firmware, capacity, interface, link speed, TRIM...
  - **Log**: app starts and alerts
- Works with SATA and NVMe drives, including SATA drives behind a RAID/RST controller
- Alerts only when something needs action: estimated remaining life under 6 months, a failed
  S.M.A.R.T. self-assessment, or low spare capacity. Problems that can be one-offs only alert
  when they repeat: pending/reallocated sectors and media errors once they have grown twice
  (or reached 10), and temperature once it has stayed critical for 3 readings in a row. A
  single pending sector, or a merely worn drive with plenty of life left, does not nag you.
- Log, S.M.A.R.T. and Information lists can be copied (Ctrl+C, Ctrl+A, or right-click)
- Temperature and health history is stored in `%ProgramData%\DiskSentinel`
- Starts automatically at logon via an elevated scheduled task (no UAC prompt each login)

Drive data comes from [smartmontools](https://www.smartmontools.org/) (`smartctl`).

## Requirements

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- smartmontools (the installer adds it with winget if missing)

## Install

**From a release:** download the zip from the
[Releases](https://github.com/cilmeron/DiskSentinel/releases) page, extract it, and run
`scripts\install.ps1` (needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)).

**From source**, in a PowerShell prompt in the repo:

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

Thresholds, the 6-month warning window and the refresh interval are constants in
[`src/Settings.cs`](src/Settings.cs).

## How health and remaining life are calculated

Health starts at 100% and is capped by the drive's remaining-life value (NVMe
`percentage_used`, or the vendor life attributes on SATA SSDs). It is reduced for
reallocated/pending sectors and media errors, and capped low if the S.M.A.R.T.
self-assessment fails or spare capacity runs out.

Estimated remaining lifetime extrapolates the average wear rate since the drive was new
(`power-on time x life left / life used`). It is a rough guide: real wear depends on your
future workload. This is a heuristic, not a guarantee. Keep backups.

## License

MIT
