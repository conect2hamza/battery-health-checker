# Portable Laptop Battery Health Checker — Software Audit

| | |
|---|---|
| **Version audited** | 1.0.1 (`eb9e32b`) |
| **Date** | 15 September 2026 |
| **Reviewer** | Claude Opus 5 — **the author of the code under review** |
| **Method** | Source inspection + executed probes against the calculation and merge layers |
| **Overall status** | **NOT READY** |

---

## 0. Auditor independence — read this first

This audit was performed by the same agent that wrote the code. That is a material
defect in the audit, not a formality.

Concretely, this reviewer already shipped release 1.0.0, which **could not start on any
machine**, and a 92-test suite passed it green. The failure mode was not subtle: every
launch ended in a message box. It was missed because the tests measured the layers the
author found interesting rather than the ones that could break.

Treat the findings below as useful and the clean bills of health as unverified. An
independent reviewer, and a real laptop, are both still required.

---

## 1. Executive summary

| Score | Value | Basis |
|---|---:|---|
| Overall software quality | **62 / 100** | Sound architecture, two accuracy defects, no hardware validation |
| Functional reliability | **55 / 100** | One shipped build could not start; disposal race still open |
| Battery-data accuracy | **55 / 100** | BUG-001 and BUG-004 both present wrong information |
| Calculation accuracy | **95 / 100** | All eight specified cases verified by execution |
| Security | **90 / 100** | No network, no secrets, injection handled; binary unsigned |
| Performance | **NOT TESTED** | No Windows machine available to measure |
| UI/UX | **50 / 100** | Never seen by a human; crash fixed, layout unverified |
| Compatibility | **NOT TESTED** | Zero manufacturer coverage |
| Portable EXE reliability | **85 / 100** | Single-file contract machine-verified every build; never launched |

### Status: `NOT READY`

Three reasons, in order:

1. **Two High-severity accuracy defects display incorrect information.** A machine with a
   UPS attached shows the UPS's health as the laptop's battery health (BUG-001). A real
   milliwatt-hour capacity can be labelled "relative units" (BUG-004). For a diagnostic
   tool, presenting a confidently wrong number is worse than presenting nothing.
2. **No value has ever been read from real battery hardware.** Every number in this audit
   came from synthetic inputs. The four-source collector chain is untested against the
   ACPI implementations it exists to accommodate.
3. **Stability is unproven.** The 1.0.0 startup crash is fixed and guarded, but a
   disposal race remains open (BUG-003) and no soak testing has occurred.

Calculations, privacy and the portable-EXE contract are genuinely solid. They are not
what is blocking release.

---

## 2. Architecture review

```text
Windows hardware
      |
      |  IOCTL_BATTERY_QUERY_INFORMATION / _STATUS   (Native/, Collectors/)
      |  root\WMI ACPI classes
      |  Win32_Battery / Win32_PortableBattery
      |  GetSystemPowerStatus
      v
Collectors           one per interface; contractually never throw
      v
BatteryRepository    cross-source merge, ranked per field by DataSource
      v
Finalize()           validation, derived values, SRS 20 sanity checks
      v
BatteryHealthCalculator   pure function; no hardware access
      v
BatteryService       orchestration; always off the UI thread
      v
UI/ + Reporting/     render snapshots only
```

**Strength.** `Measured<T>` makes availability and provenance part of every reading, so
"the hardware did not report this" is representable and cannot silently become `0`. This
is the single best decision in the codebase and it is what makes the no-fabrication rule
enforceable rather than aspirational.

**Weakness 1 — the merge layer carries too much semantic weight.** `BatteryRepository`
decides battery identity, field precedence, unit assignment and derived values in one
pass. Both High-severity defects live here. It needs to be split: identity resolution,
field merge, and unit resolution are three different problems.

**Weakness 2 — `DataSource.Calculated = 5` outranks `BatteryIoctl = 4`.** This is correct
for charge percentage and accidental everywhere else. Precedence should be per-field, not
a single global ordering.

**Weakness 3 — capability flags are collected and discarded.** `IsSystemBattery` is read
from the driver and then ignored (BUG-001). `ReportedNoBattery` is written and never read
(BUG-002).

---

## 3. Battery detection audit

| Property | Source | Available | Validation | Issues |
|---|---|---|---|---|
| Design capacity | IOCTL `BatteryInformation` → ACPI `BatteryStaticData` → `Win32_PortableBattery` | UNVERIFIED on hardware | `> 0` enforced | Unit may be mislabelled — BUG-004 |
| Full charge capacity | IOCTL → ACPI `BatteryFullChargedCapacity` | UNVERIFIED | `> 0` enforced | Same |
| Remaining capacity | IOCTL `BATTERY_STATUS.Capacity` → ACPI | UNVERIFIED | `>= 0` enforced | — |
| Charge % | Derived from remaining/full; falls back to OS estimate | Verified by probe | Clamped 0–100 | Lost entirely when source battery counts disagree |
| Cycle count | IOCTL → `BatteryCycleCount` | UNVERIFIED | `0` treated as not-tracked | Correct — a firmware `0` is not a real zero |
| Voltage | IOCTL → ACPI | UNVERIFIED | `> 0`, sentinel rejected | — |
| Temperature | IOCTL `BatteryTemperature` → ACPI | UNVERIFIED | 213–423 K window | — |
| Runtime | IOCTL `BatteryEstimatedTime` → ACPI → `GetSystemPowerStatus` | UNVERIFIED | ≤ 7 days; suppressed while charging | — |
| Chemistry | 4-char ACPI code → CIM enum | Mapping unit-tested | Unknown codes shown verbatim | — |
| Serial / manufacturer / model | IOCTL strings → ACPI → SMBIOS | UNVERIFIED | Null/whitespace → Not Available | — |
| **Is system battery** | IOCTL / ACPI capability bit | **Read and then ignored** | **None** | **BUG-001** |

> `NOT TESTED — Evidence/environment unavailable` for every row marked UNVERIFIED. No
> battery hardware was reachable from the audit environment.

---

## 4. Calculation verification — EXECUTED

Run against the shipped `BatteryHealthCalculator`. These are real outputs, not expectations.

| # | Design | Full charge | Expected | Actual health | Wear | Grade | Flagged | Raw preserved |
|---|---:|---:|---|---|---|---|---|---|
| 1 | 100,000 | 100,000 | 100% | 100% | 0% | EXCELLENT | no | 100.00% |
| 2 | 100,000 | 90,000 | 90% | 90% | 10% | EXCELLENT | no | 90.00% |
| 3 | 60,000 | 48,000 | 80% | 80% | 20% | GOOD | no | 80.00% |
| 4 | 50,000 | 25,000 | 50% | 50% | 50% | POOR | no | 50.00% |
| 5 | 50,000 | 0 | 0% | 0% | 100% | CRITICAL | no | 0.00% |
| 6 | 0 | 0 | N/A | Not Available | Not Available | `NonPositiveDesignCapacity` | no | — |
| 7 | 50,000 | −1,000 | Invalid | Not Available | Not Available | `NoFullChargeCapacity` | no | — |
| 8 | 50,000 | 60,000 | >100% | 100% | 0% | EXCELLENT | **YES** | **120.00%** |

**All eight pass.** Case 8 answers the question §25 poses: the raw 120% is preserved and
reported, the display is capped, and the reading is flagged as implausible. Abnormal
hardware data is not silently hidden.

Classification boundaries probed exactly: `100→Excellent, 90→Excellent, 89.9999→Good,
80→Good, 79.9999→Fair, 60→Fair, 59.9999→Poor, 40→Poor, 39.9999→Critical, 0→Critical`.
Matches the specified thresholds with no off-by-one.

Extreme-value probes — `long.MaxValue` numerator, denominator, and both — produced no
`NaN`, no `Infinity`, and no out-of-range display value.

**Thresholds are application-defined, not an industry standard.** The application says so
in the About screen and in every report footer.

---

## 5. Merge-layer probes — EXECUTED

| Probe | Result | Verdict |
|---|---|---|
| A — IOCTL declares Relative, ACPI supplies 60,000 mWh | `unit=Relative`, displayed **"60,000 (relative units)"** | **BUG-004 — High** |
| B — IOCTL sees 2 batteries, Win32 sees 1 | Both batteries: charge `Not Available` | Correct (fail-closed), but see IMPROVEMENT-001 |
| C — remaining from IOCTL, full-charge from ACPI | `86.78%`, source `Calculated` | Correct |
| D — UPS (non-system, 50%) enumerated before laptop (95%) | Dashboard shows **"APC Back-UPS" health 50%** first | **BUG-001 — High** |
| E — state `Charging` with AC `Disconnected` | Both displayed verbatim | **BUG-005 — Medium** |
| F — ACPI lists a device, `GetSystemPowerStatus` says no battery | Phantom presented as real, `HasBattery=True` | **BUG-002 — Medium** |

---

## 6. Security audit

| Check | Result | Evidence |
|---|---|---|
| Network activity | **None** | Zero occurrences of `HttpClient`, `WebClient`, `Socket`, `TcpClient`, `WebRequest`, or any `http(s)://` literal across 7,786 lines |
| Command injection / shell exec | None | Single `Process.Start`, on a user-chosen directory, `Directory.Exists`-guarded |
| Hard-coded secrets / API keys | None | No credential patterns present |
| Hard-coded developer paths | **None** | No `C:\`, `/home/`, `/Users/` literals |
| Unsafe deserialization | None | `System.Text.Json`, no polymorphic binding |
| CSV formula injection | **Mitigated** | Leading `= + - @` prefixed with `'`; unit-tested |
| HTML injection from firmware strings | **Mitigated** | `WebUtility.HtmlEncode` on all values; unit-tested |
| Privilege requirement | `asInvoker` | No UAC prompt; admin never required |
| DLL hijacking surface | Minimal | Self-contained single file; all P/Invoke to `kernel32`/`setupapi` |

### SECURITY-001 — Binary is unsigned
**Severity:** Medium · **Status:** Open

No Authenticode signature. Windows SmartScreen warns on every download, and users are
being trained to click through that warning. Publisher identity is unverifiable.
**Recommendation:** OV or EV code-signing certificate before wider distribution.

### SECURITY-002 — `UseShellExecute` on a stored directory path
**Severity:** Low · **Status:** Open

`ReportsView.OpenReportFolder` calls `Process.Start` with `UseShellExecute = true` on
`Settings.ReportDirectory`. The path is user-chosen and existence-checked, so exploitation
requires the user to have already selected a malicious target. **Recommendation:** pass
the path as an argument to `explorer.exe` rather than as the shell verb target.

---

## 7. Privacy audit

| | |
|---|---|
| **Data collected** | Battery identity, capacity, electrical state; computer name, Windows version, OEM manufacturer/model |
| **Data stored** | Settings in `%APPDATA%\BatteryHealthChecker\settings.json` (created only on change). Optional log in `%LOCALAPPDATA%\...\logs`, **off by default**. Reports only where the user saves them. |
| **Data transmitted** | **None** |
| **Data shared** | **None** |

> **No network transmission detected.** Verified by exhaustive source scan for network
> types and URL literals — zero hits. This is a source-level verification; runtime packet
> capture on Windows is `NOT TESTED`.

All eight log statements were enumerated. They record scan durations, collector names,
exception type and message, report format, and settings-IO failures. **No battery serial
number, unique ID, device path or user identifier is ever logged.**

---

## 8. Bug list

## BUG-001
### Title
Non-system batteries (UPS, peripherals) are presented as the laptop battery
### Severity
**High**
### Category
Functional / Calculation / Accuracy
### Description
The battery device interface reports a `BATTERY_SYSTEM_BATTERY` capability bit
distinguishing the system pack from attached non-system batteries — a UPS, a wireless
peripheral, some docks. The application reads this bit into `BatteryInfo.IsSystemBattery`
and emits it in JSON reports, but never uses it to filter, sort or label. Whichever
device Windows enumerates first becomes "Battery 1" and drives the dashboard headline.
### Steps to Reproduce
1. Attach a UPS or other battery device Windows exposes through the battery class.
2. Launch the application.
3. Observe the Dashboard headline.
### Expected Behavior
The laptop's system battery is shown. Non-system batteries are excluded, or clearly
labelled as not the system battery.
### Actual Behavior
Probe D: a UPS at 50% health is shown first, and its 50% is presented as "Battery Health"
above the laptop pack at 95%.
### Root Cause
`BatteryRepository.Merge` orders by collector enumeration ordinal and never consults
`IsSystemBattery`.
### Recommended Fix
In `Merge`, order system batteries first; exclude non-system batteries from the default
view and surface them only in Battery Details with an explicit label.
### Verification
Extend `BatteryRepositoryTests` with a mixed system/non-system set and assert the system
battery is index 0 and that a non-system battery never drives the headline.
### Status
**Open**

---

## BUG-002
### Title
`ReportedNoBattery` is written by the power-status collector and never read
### Severity
Medium
### Category
Functional
### Description
`SystemPowerStatusCollector` sets `CollectionResult.ReportedNoBattery` when Windows
positively reports `BATTERY_FLAG_NO_BATTERY`. `BatteryRepository` ignores the field. The
desktop case works only incidentally, because no collector returns a battery.
### Steps to Reproduce
1. A machine where a stale or phantom ACPI battery device is enumerated while
   `GetSystemPowerStatus` reports no system battery. (Probe F reproduces synthetically.)
### Expected Behavior
The authoritative "no system battery" signal reconciles against phantom devices.
### Actual Behavior
`HasBattery=True`, phantom device presented as a real battery with real-looking fields.
### Root Cause
Dead signal — written, never consumed.
### Recommended Fix
Consume it in `Merge`: when `ReportedNoBattery` is set and no higher-authority source
produced a *system* battery, return an empty snapshot with an explanatory issue.
### Verification
Probe F must yield `HasBattery=False`.
### Status
**Open**

---

## BUG-003
### Title
Closing the window during a scan raises an error dialog after the application has closed
### Severity
Medium
### Category
Functional / Stability
### Description
`MainForm.RunScanAsync` awaits a background scan, then unconditionally touches
`_refreshButton` in its `finally`. If the user closes the window while a scan is in
flight, `FormClosing` cancels and `Dispose` runs; the continuation then resumes and
writes to disposed controls.
### Steps to Reproduce
1. Enable auto-refresh at 1 second (or click Refresh on a machine with slow WMI).
2. Close the window while a scan is running.
### Expected Behavior
The window closes silently.
### Actual Behavior
`ObjectDisposedException` on the UI thread, caught by the global handler, which shows
"Battery Health Checker ran into a problem" *after* the application has closed.
### Root Cause
No `IsDisposed` / `Disposing` guard after the `await`; `_shutdown` is disposed while a
scan may still be draining.
### Recommended Fix
Return early after the `await` when `IsDisposed || Disposing`, and guard the `finally`.
### Verification
Automated: a test that starts a scan, disposes the form, and asserts no exception
surfaces. Manual: rapid open/close with 1-second auto-refresh.
### Status
**Open**

---

## BUG-004
### Title
Real milliwatt-hour capacities can be displayed as "relative units"
### Severity
**High**
### Category
Calculation / UI accuracy
### Description
`CapacityUnit` is taken from the anchor source — the highest-authority collector that saw
*any* battery — rather than from the source that actually supplied the winning capacity
value. A device that advertises `BATTERY_CAPACITY_RELATIVE` but reports no usable
capacity sets the unit to `Relative`; a genuine mWh value merged in from the ACPI class is
then labelled with it.
### Steps to Reproduce
1. Probe A: IOCTL collector reports `CapacityUnit.Relative` and no capacities; ACPI
   collector reports design 60,000 mWh.
### Expected Behavior
`60,000 mWh`
### Actual Behavior
`60,000 (relative units)`
### Root Cause
`BatteryRepository.Fold` assigns unit per-battery from the anchor instead of pairing each
capacity reading with the unit of its own source.
### Recommended Fix
Carry the unit alongside each capacity reading and resolve it with the value that wins the
merge, not with the anchor record.
### Verification
Probe A must display `60,000 mWh`.
### Status
**Open**

---

## BUG-005
### Title
Contradictory charge state and AC state are displayed together
### Severity
Medium
### Category
UI / Logic
### Description
SRS §8 requires the UI never to display contradictory information. `Finalize` reconciles
charge state against the sign of the power-flow rate only when the state is already
`Unknown`, so a source reporting `Charging` while AC is `Disconnected` passes through.
### Steps to Reproduce
1. Probe E: state `Charging`, AC `Disconnected`.
### Expected Behavior
The pair is reconciled, or the inconsistency is flagged.
### Actual Behavior
"Status: Charging" and "AC Power: Disconnected" shown side by side.
### Root Cause
Reconciliation guarded on `ChargeState.Unknown` rather than applied whenever a
higher-authority signal contradicts a lower one.
### Recommended Fix
Treat a signed rate as authoritative over a reported state, and raise a collection issue
when sources genuinely disagree.
### Verification
Probe E must produce a consistent pair or an explicit warning.
### Status
**Open**

---

## BUG-006
### Title
Negative full-charge capacity reports a misleading reason code
### Severity
Low
### Category
Calculation
### Description
A negative full-charge reading yields `HealthUnavailableReason.NoFullChargeCapacity`. The
value *was* reported; it was invalid. The outcome is correct (Not Available, no crash) but
the reason appears in JSON reports as `unavailableReason` and misstates the cause.
### Recommended Fix
Add `InvalidFullChargeCapacity`.
### Status
Open

---

## 9. Improvements

## IMPROVEMENT-001
### Area
Cross-source battery correlation
### Current Behavior
When two sources report different battery counts and share no unique ID, the merge
declines to attribute readings (probe B). On a dual-battery machine where only
`Win32_Battery` reports charge level, the user sees `Not Available`.
### Recommended Behavior
Keep the fail-closed default, but surface whole-system readings as an explicitly labelled
system-wide figure rather than discarding them.
### Priority
Medium
### Reason
Refusing to guess is right; silently losing available data is not.
### Status
Planned

## IMPROVEMENT-002
### Area
Field precedence
### Current Behavior
A single global `DataSource` ordering governs every field, and `Calculated` outranks all
hardware sources.
### Recommended Behavior
Per-field precedence.
### Priority
Medium
### Status
Planned

## IMPROVEMENT-003
### Area
Dead code
### Current Behavior
`Measured.Select` and `Measured.FromNullable` are used only by tests.
`CollectionResult.ReportedNoBattery` is written and never read.
### Recommended Behavior
Consume `ReportedNoBattery` (BUG-002); remove or use the unused helpers.
### Priority
Low
### Status
Planned

---

## 10. Dependency audit

| Dependency | Version | Purpose | Required | Security risk | Recommendation |
|---|---|---|---|---|---|
| `System.Management` | 8.0.0 | WMI / CIM access | Yes | None known | Keep at 8.0.0 for `net8.0` |
| `System.CodeDom` | 8.0.0 | Transitive of above | Transitive | None known | No action |
| `Microsoft.NET.ILLink.Tasks` | 8.0.31 | Build-time trimming tooling | Build only | None | Does not ship; harmless |

Scanned against `api.nuget.org`: **no vulnerable packages, no deprecated packages.**
Newer 10.0.x releases exist but target .NET 10; 8.0.0 is correct for this TFM.

Total third-party runtime surface: **two Microsoft-published assemblies.**

---

## 11. Build and packaging audit

| Check | Status |
|---|---|
| Release build | Pass — CI, 0 warnings, 0 errors |
| Debug code / debug logs removed | Pass — logging off by default, `DebugType=none` |
| Developer paths removed | Pass — verified, none present |
| Single-file, self-contained | **Pass — machine-verified every build** |
| Architecture | `win-x64` + `win-arm64` |
| Version info / product name / icon / manifest | Present |
| Digital signature | **Absent — SECURITY-001** |
| Antivirus false-positive risk | **UNVERIFIED** — unsigned, compressed single-file bundles are a common heuristic trigger |
| Clean-machine launch | `NOT TESTED — Evidence/environment unavailable` |

CI evidence, verbatim:

```text
Published files:
  70,467,815  BatteryHealthChecker.exe
BatteryHealthChecker.exe = 67.2 MB
```

One file. No DLL, no runtime, no config, no `.pdb`. The build fails if anything else
appears beside the EXE.

---

## 12. Testing status

**97 automated tests, all passing on `windows-latest`.**

| Layer | Covered |
|---|---|
| Health calculation | 14 cases incl. all §25 rows and boundaries |
| `Measured<T>` | 6 |
| Merge / validation | 11 |
| Formatting | 10 |
| Chemistry / status mapping | 18 |
| Report writers | 13 |
| Settings | 5 |
| **UI construction and paint** | **5** — every control and view, both themes, five snapshot shapes |

### NOT TESTED — Evidence/environment unavailable

| Section | Item |
|---|---|
| §13 | Every manufacturer — Dell, HP, Lenovo, ASUS, Acer, MSI, Surface, Samsung, Toshiba, Framework |
| §14 | USB / external drive / read-only folder / path with spaces / moved EXE / non-English username |
| §22 | Startup time, memory, CPU, idle draw, refresh cost |
| §23 | 100+ launches, 100+ refreshes, sleep/wake, AC connect/disconnect, battery removal |
| §24 | High DPI, low-resolution display |
| §29 | Clean Windows installation |
| — | **Any reading from real battery hardware** |

---

## 13. Final scorecard

| Category | Score |
|---|---:|
| Battery detection | 70 |
| Battery accuracy | 55 |
| Calculations | 95 |
| Hardware compatibility | **NOT TESTED** |
| Windows compatibility | **NOT TESTED** |
| Portable EXE | 85 |
| Security | 90 |
| Privacy | 95 |
| Performance | **NOT TESTED** |
| Stability | 40 |
| UI/UX | 50 |
| Code quality | 80 |
| Documentation | 85 |
| **Overall** | **62 / 100** |

Categories marked NOT TESTED are deliberately unscored. Assigning a number to an untested
category would be inventing a test result.

---

## 14. Release recommendation

### NOT READY

Not `BLOCK RELEASE`: there is no security vulnerability, no data loss, no incorrect
health arithmetic, and no privacy leak. The calculation engine is verified correct.

Not `APPROVED WITH CONDITIONS` either, because two open defects cause the application to
display information that is **confidently wrong** — the wrong battery's health (BUG-001),
and a real capacity under a fabricated unit (BUG-004). A battery diagnostic tool that
misreports is worse than one that reports nothing, and both defects violate the
application's own stated principle that no value is ever invented.

Add to that: no reading has ever been taken from real hardware, and the immediately
preceding release could not start.

**Required before release:** fix BUG-001 and BUG-004; run on at least three laptops from
different manufacturers; confirm design capacity, full-charge capacity and cycle count are
actually populated.

---

## 15. Priority fix list

### 🔴 Critical — fix immediately
- [ ] Nothing. No security, data-loss or arithmetic defect found.

### 🟠 High priority
- [ ] **BUG-001** — filter/label non-system batteries so a UPS cannot masquerade as the laptop pack
- [ ] **BUG-004** — bind capacity unit to the source of the winning value, not to the anchor
- [ ] Run on real hardware from three or more manufacturers and record what is populated

### 🟡 Medium priority
- [ ] **BUG-002** — consume `ReportedNoBattery`
- [ ] **BUG-003** — guard the post-`await` continuation against a disposed form
- [ ] **BUG-005** — reconcile contradictory charge/AC state
- [ ] **SECURITY-001** — obtain a code-signing certificate
- [ ] **IMPROVEMENT-001** — surface whole-system readings instead of discarding them

### 🔵 Low priority
- [ ] **BUG-006** — add `InvalidFullChargeCapacity`
- [ ] **SECURITY-002** — launch Explorer with the folder as an argument
- [ ] **IMPROVEMENT-003** — remove dead code

### 💡 Future
- [ ] Independent review by someone who did not write the code
- [ ] Performance instrumentation (startup, idle CPU, refresh cost)
- [ ] Screenshot-diff tests over `DrawToBitmap` to catch layout regressions
- [ ] Optional `powercfg /batteryreport` cross-check to validate reported capacities

---

## 16. Review history

| Version | Date | Reviewer | Changes | Critical issues | Status |
|---|---|---|---|---|---|
| 1.0.0 | 2026-09-15 | — | Initial release | 1 — startup crash, found by user on first launch | Superseded |
| 1.0.1 | 2026-09-15 | Claude Opus 5 (author) | Startup crash fixed; UI smoke tests added; first full audit | 0 critical, 2 high | **NOT READY** |

Historical issues are never deleted. They are marked Fixed / Verified / Closed / Won't Fix.

### Closed in 1.0.1

**BUG-000 — Application could not start (Critical, Fixed, Verified).** Four controls
deriving from `Control` assigned `Color.Transparent` without setting
`ControlStyles.SupportsTransparentBackColor`, which `Control` rejects at construction.
Every launch of 1.0.0 failed. Fixed in `eb9e32b`. Verified by `UiSmokeTests`, which now
constructs and paints every control and view. **Root cause of the escape:** the test suite
covered calculation, merging and reporting and never instantiated a UI object.
