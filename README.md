# Battery Health Checker

A portable Windows laptop battery diagnostic utility. One executable, no installer, no
runtime to install, no network access.

```
BatteryHealthChecker.exe
```

Download it, double-click it, and it opens.

---

## What it does

Reads the battery information Windows and the battery firmware actually expose, and
presents it as health, wear, capacity and live status:

| | |
|---|---|
| **Health** | `(full charge capacity / design capacity) x 100`, graded Excellent / Good / Fair / Poor / Critical |
| **Capacity** | design, full-charge and current capacity, with a visual comparison |
| **Status** | charge level, charging state, AC power, estimated runtime |
| **Detail** | manufacturer, model, serial, chemistry, cycle count, voltage, power flow, temperature |
| **Reports** | HTML, TXT, CSV and JSON, saved wherever you choose |

**Values this computer does not report are shown as `Not Available`.** Never as `0`, never
as an estimate presented as a measurement. Health itself is labelled as an estimate derived
from firmware-reported capacity, because that is what it is.

## Getting the executable

Every push builds it. Download the artifact from the
[Build portable EXE](../../actions/workflows/build.yml) run for your commit, or grab
`BatteryHealthChecker-win-x64.exe` from a [release](../../releases).

Building it yourself needs the .NET 8 SDK **on Windows** (WinForms cannot be published
from Linux or macOS):

```powershell
dotnet publish src/BatteryHealthChecker/BatteryHealthChecker.csproj -c Release -r win-x64 -o publish
```

`publish/BatteryHealthChecker.exe` is the whole deliverable.

## Requirements

- Windows 10 (build 10240 or later) or Windows 11, x64 or arm64
- A standard user account. **Administrator is not required** and the app never asks for it.
- No internet connection, at any point.

## How it reads the battery

Four independent Windows sources are queried and merged, so a value missing from one can
be supplied by another. The merge prefers the more authoritative source per field:

| Priority | Source | What it is good for |
|---|---|---|
| 1 | `IOCTL_BATTERY_QUERY_INFORMATION` / `_STATUS` on the battery device interface | design and full-charge capacity, cycle count, chemistry, serial, temperature, voltage, power flow |
| 2 | `root\WMI` ACPI classes (`BatteryStaticData`, `BatteryFullChargedCapacity`, `BatteryCycleCount`, `BatteryStatus`, `BatteryRuntime`, `BatteryTemperature`) | fills gaps left by vendors who populate one interface and not the other |
| 3 | `Win32_Battery` / `Win32_PortableBattery` (CIM) | SMBIOS manufacturer and model, charge level |
| 4 | `GetSystemPowerStatus` | always answers; the authority on "this machine has no battery" |

`Win32_Battery.DesignCapacity` is null on almost every real laptop, which is why the driver
interface leads and this tool does not rely on WMI alone.

## Architecture

```
Windows hardware
      |
Collectors          Native/, Collectors/       one per Windows interface, never throw
      |
BatteryRepository   Services/                  cross-source merge + validation
      |
BatteryHealthCalculator                        pure function, fully unit tested
      |
BatteryService                                 orchestration, always off the UI thread
      |
UI/ + Reporting/                               render snapshots; no hardware access
```

Two design decisions carry most of the correctness weight:

- **`Measured<T>`** (`Models/Measured.cs`) makes "unavailable" a state of every single
  reading, alongside the source it came from. A view cannot render a missing value as `0`
  without going out of its way, which is how the no-fabrication rule is actually enforced
  rather than merely intended.
- **Status refresh is separated from static data.** Auto-refresh re-reads only the fast
  electrical status and carries the static facts over from the last full scan, so a
  one-second interval does not mean a WMI round trip every second.

## Privacy

Telemetry: **Disabled**. Cloud upload: **None**.

No network requests, no account, no registry writes, no background service, no changes to
Windows, BIOS, security or charging settings. Settings are stored per user in
`%APPDATA%\BatteryHealthChecker\settings.json`, created only when you change something -
the application starts and runs correctly without it. Optional debug logging is off by
default and writes only to `%LOCALAPPDATA%\BatteryHealthChecker\logs`.

## Development

```bash
dotnet build BatteryHealthChecker.sln -c Release
dotnet test tests/BatteryHealthChecker.Tests/BatteryHealthChecker.Tests.csproj
```

The application icon is a committed binary that gets compiled into the EXE. Regenerate it
with `python3 tools/make_icon.py` (standard library only).

## Verification status

Two documents, kept reconciled:

- **[docs/audit.md](docs/audit.md)** — the bug register. Every defect ever raised, its
  severity, root cause, and whether the fix is *verified* or merely *believed correct*.
- **[docs/acceptance.md](docs/acceptance.md)** — requirement coverage against the spec.

Read the short version first: **no value has ever been read from real battery hardware.**
Everything verified in this project was verified against synthetic inputs. The suite is
green on Windows and one earlier release still could not start.
