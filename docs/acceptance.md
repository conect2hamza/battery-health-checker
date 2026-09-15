# Acceptance checklist

Point-by-point status against section 31 of the SRS.

Three states are used, and the distinction matters:

- **Implemented + tested** — code exists and an automated test or an inspected build output covers it.
- **Implemented** — code exists and compiles, but nothing has exercised it against real hardware.
- **Not verified** — requires a physical Windows laptop; cannot be done in CI.

## Single-EXE and portability

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 1 | Single standalone EXE | Implemented + tested | `PublishSingleFile` + `SelfContained` + `IncludeNativeLibrariesForSelfExtract`; the CI step **Verify single-file contract** fails the build if the publish folder holds anything but the EXE (and a `.pdb`) |
| 2 | Portable | Implemented | No install step, no registry writes, no fixed paths |
| 3 | No installer | Implemented + tested | CI publishes a bare EXE; there is no installer project |
| 4 | No separate runtime | Implemented + tested | `SelfContained=true`; verified by the same CI step |
| 5 | No required external DLLs | Implemented + tested | Same CI step |
| 6 | Works offline | Implemented + tested | No HTTP client or socket anywhere in the source; `ReportWriterTests.Html_is_self_contained_with_no_external_resources` asserts reports carry no external URLs |
| 7 | Windows 10 supported | Implemented | `supportedOS` in `app.manifest`; build-number guard in `Program.cs` |
| 8 | Windows 11 supported | Implemented | Same, plus build >= 22000 detection in `SystemInfoService` |

## Battery data

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 9 | Battery detected correctly | Implemented | Four-source collector chain; **not verified on hardware** |
| 10 | Battery percentage shown | Implemented | Derived from this battery's own capacities when possible; `BatteryRepositoryTests.Charge_percent_is_derived_from_this_batterys_own_capacities` |
| 11 | Design capacity shown | Implemented | IOCTL `BatteryInformation.DesignedCapacity`, with three fallbacks |
| 12 | Full-charge capacity shown | Implemented | IOCTL + `BatteryFullChargedCapacity` |
| 13 | Current capacity shown | Implemented | IOCTL `BATTERY_STATUS.Capacity` |
| 14 | Health calculated correctly | Implemented + tested | `BatteryHealthCalculatorTests` - 14 cases including the SRS worked example |
| 15 | Wear calculated correctly | Implemented + tested | Same |
| 16 | Charging status shown | Implemented + tested | `ChemistryMappingTests.Maps_Win32_Battery_status_codes` |
| 17 | AC status shown | Implemented + tested | Same, plus `GetSystemPowerStatus` |
| 18 | Cycle count when available | Implemented + tested | A firmware `0` is treated as "not tracked", not as a real zero |
| 19 | Temperature when available | Implemented + tested | Kelvin-to-C/F conversion tested; readings outside -60 C..+150 C rejected |
| 20 | Voltage when available | Implemented + tested | `BatteryStatusServiceTests.Voltage_is_shown_in_volts` |
| 21 | Runtime when available | Implemented + tested | Formatting and the Calculating/Not Available distinction tested; suppressed while charging |
| 22 | Multiple batteries supported | Implemented + tested | `BatteryRepositoryTests` covers separate health figures and refusing to attribute a whole-system reading to one pack |
| 23 | Missing values handled | Implemented + tested | `MeasuredTests` + `BatteryStatusServiceTests` |
| 24 | No fabricated values | Implemented + tested | `Measured<T>` makes it a type-level property; JSON emits no `value` key at all when unavailable |

## Features

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 25 | Reports supported | Implemented + tested | HTML, TXT, CSV, JSON; `ReportWriterTests` (13 cases) |
| 26 | Manual refresh | Implemented | Refresh button and F5 |
| 27 | Optional auto-refresh | Implemented | 1/5/10/30/60 s; uses the status-only fast path |

## Safety and robustness

| # | Requirement | Status | Evidence |
|---|---|---|---|
| 28 | Privacy respected | Implemented | No network code; stated in-app and in every report |
| 29 | No unnecessary network requests | Implemented + tested | No network types referenced outside `System.Net.WebUtility.HtmlEncode` (a string function) |
| 30 | No hidden services | Implemented | Single process, no service installation |
| 31 | No registry installation | Implemented | Registry is read-only (theme preference, Windows edition name) |
| 32 | No system modifications | Implemented | No BIOS, charging-limit or security calls anywhere |
| 33 | Error handling implemented | Implemented | Collectors never throw; `Program.cs` installs both unhandled-exception handlers |
| 34 | Clean Windows test completed | **Not verified** | Needs a machine without the .NET SDK |
| 35 | USB portable test completed | **Not verified** | Needs physical media |

## What has not been done

These need a real Windows laptop and cannot be closed from CI:

1. **SRS 27 hardware matrix** — Dell, HP, Lenovo, ASUS, Acer, MSI, Surface. Vendor ACPI
   implementations differ in exactly the places this tool reads, so this is the test that
   matters most and the one that has not happened.
2. **Clean-machine and USB execution** (SRS 28) — the CI check proves the publish output
   is a lone EXE, not that it launches on a machine with no runtime installed.
3. **Live states** — charging, discharging, fully charged, AC connected/disconnected,
   degraded and severely degraded batteries, and a genuine multi-battery machine.
4. **Standard-user vs administrator behaviour** on hardware that actually denies access,
   which is the path that decides whether the administrator hint is ever shown.

The first run on real hardware is where the ACPI edge cases will surface. Everything above
is written to degrade to `Not Available` rather than crash when they do.

## Startup crash in 1.0.0, fixed in 1.0.1

Version 1.0.0 failed on launch with *"Control does not support transparent background
colors."* Four custom controls assigned `Color.Transparent` without first setting
`ControlStyles.SupportsTransparentBackColor`, which a bare `Control` rejects at
construction; `Panel`, `TableLayoutPanel`, `UserControl` and `ButtonBase` set that flag
themselves, which is why the controls derived from those were unaffected.

The real failure was in the tests, not the fix: 92 tests covered the calculation,
merging and reporting layers and not one of them constructed a control, so a defect that
made the application unable to start got a green build. `UiSmokeTests` now constructs and
paints every control and every view, in both themes, against five snapshot shapes -
normal, second battery selected, no battery, nothing reported, and an implausible
reading. Constructing a control catches this class of defect; painting it catches the
next one.
