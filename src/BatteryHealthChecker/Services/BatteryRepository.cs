using BatteryHealthChecker.Collectors;
using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Services;

/// <summary>
/// Merges the per-source collection results into one snapshot (SRS 4: "do not rely on
/// a single data source if Windows exposes information through multiple mechanisms").
///
/// The merge rule is simple and explicit: the highest-authority source that saw any
/// battery at all defines how many batteries exist and their identity; every other
/// source may only fill in readings that the anchor left unavailable, or replace one
/// whose source ranks higher for that particular field.
/// </summary>
internal static class BatteryRepository
{
    public static BatterySnapshot Merge(IReadOnlyList<CollectionResult> results)
    {
        var issues = new List<CollectionIssue>();
        foreach (CollectionResult result in results) issues.AddRange(result.Issues);

        // Deduplicate: the same access problem reported by two collectors reads as noise.
        List<CollectionIssue> distinctIssues = issues
            .GroupBy(i => (i.Message, i.ElevationMayHelp))
            .Select(g => g.First())
            .ToList();

        AcPowerState ac = results
            .OrderByDescending(r => HighestSource(r))
            .Select(r => r.AcPower)
            .FirstOrDefault(state => state != AcPowerState.Unknown);

        // SRS 19 / BUG-002: GetSystemPowerStatus is the authority on "this machine has
        // no system battery". It is consumed below to reject phantom device entries.
        bool systemReportsNoBattery = results.Any(r => r.ReportedNoBattery);

        List<CollectionResult> withBatteries = results
            .Where(r => r.Batteries.Count > 0)
            .OrderByDescending(HighestSource)
            .ToList();

        if (withBatteries.Count == 0)
        {
            return new BatterySnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                Batteries = Array.Empty<BatteryReading>(),
                AcPower = ac,
                SystemReportsNoBattery = systemReportsNoBattery,
                Issues = distinctIssues,
            };
        }

        CollectionResult anchor = withBatteries[0];
        // The anchor's records are mutated in place as lower-authority sources are folded
        // in. That is safe because every collector builds fresh objects for each scan,
        // so nothing outside this merge holds a reference to them.
        List<CollectedBattery> merged = anchor.Batteries.OrderBy(b => b.Ordinal).ToList();

        foreach (CollectedBattery battery in merged)
        {
            RecordCapacityUnit(battery, battery);
        }

        foreach (CollectionResult other in withBatteries.Skip(1))
        {
            // A whole-system source cannot be attributed to one pack on a multi-battery
            // machine; using it there would put one battery's numbers on another (SRS 11).
            bool wholeSystemSource = HighestSource(other) == DataSource.SystemPowerStatus;
            if (wholeSystemSource && merged.Count > 1) continue;

            foreach (CollectedBattery candidate in other.Batteries)
            {
                CollectedBattery? target = Match(merged, candidate, other.Batteries.Count);
                if (target is not null) Fold(target, candidate);
            }
        }

        // BUG-002: when Windows positively reports no system battery, an entry that was
        // never confirmed as the system pack is a stale or phantom device, not a battery.
        if (systemReportsNoBattery)
        {
            int before = merged.Count;
            merged = merged.Where(b => b.Info.IsConfirmedSystemBattery).ToList();

            if (merged.Count < before)
            {
                distinctIssues.Add(new CollectionIssue(
                    DataSource.SystemPowerStatus,
                    $"Windows reports that this computer has no system battery, so "
                    + $"{before - merged.Count} detected battery device"
                    + $"{(before - merged.Count == 1 ? " was" : "s were")} not shown.",
                    ElevationMayHelp: false));
            }
        }

        // BUG-001: the machine's own battery leads. A UPS or peripheral pack must never
        // become "Battery 1" and drive the health headline. Ordering is stable within
        // each group, so enumeration order is preserved among equals.
        merged = merged
            .OrderByDescending(b => b.Info.IsConfirmedSystemBattery ? 2 : b.Info.IsConfirmedPeripheral ? 0 : 1)
            .ToList();

        var readings = new List<BatteryReading>(merged.Count);
        for (int i = 0; i < merged.Count; i++)
        {
            CollectedBattery battery = merged[i];
            battery.Info.Index = i;
            battery.Status.DeviceKey = battery.Info.DeviceKey;

            Finalize(battery, distinctIssues);

            readings.Add(new BatteryReading
            {
                Info = battery.Info,
                Status = battery.Status,
                Health = BatteryHealthCalculator.Calculate(
                    battery.Info.DesignCapacity, battery.Info.FullChargeCapacity),
            });
        }

        return new BatterySnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            Batteries = readings,
            AcPower = ac != AcPowerState.Unknown
                ? ac
                : readings.Select(r => r.Status.AcPower).FirstOrDefault(s => s != AcPowerState.Unknown),
            SystemReportsNoBattery = systemReportsNoBattery,
            Issues = distinctIssues,
        };
    }

    /// <summary>Notes the capacity unit a record's own source declared (BUG-004).</summary>
    private static void RecordCapacityUnit(CollectedBattery target, CollectedBattery source)
    {
        DataSource from = source.PrimarySource;
        if (from != DataSource.None && source.Info.CapacityUnit != CapacityUnit.Unknown)
        {
            target.CapacityUnitBySource[from] = source.Info.CapacityUnit;
        }
    }

    private static DataSource HighestSource(CollectionResult result)
    {
        DataSource highest = DataSource.None;
        foreach (CollectedBattery battery in result.Batteries)
        {
            foreach (DataSource source in battery.Info.ContributingSources) if (source > highest) highest = source;
            foreach (DataSource source in battery.Status.ContributingSources) if (source > highest) highest = source;
        }
        return highest;
    }

    /// <summary>
    /// Correlates a candidate with an already-merged battery: first by the hardware
    /// unique ID, then positionally when the two sources agree on how many batteries
    /// exist, and finally by the single-battery case.
    /// </summary>
    private static CollectedBattery? Match(
        List<CollectedBattery> merged, CollectedBattery candidate, int candidateCount)
    {
        if (!string.IsNullOrWhiteSpace(candidate.UniqueId))
        {
            CollectedBattery? byId = merged.FirstOrDefault(m =>
                !string.IsNullOrWhiteSpace(m.UniqueId) &&
                string.Equals(m.UniqueId, candidate.UniqueId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        if (merged.Count == candidateCount && candidate.Ordinal < merged.Count)
        {
            return merged[candidate.Ordinal];
        }

        if (merged.Count == 1 && candidateCount == 1) return merged[0];

        // Counts disagree and there is no shared identifier: guessing would risk
        // attributing one battery's readings to another, so decline the match.
        return null;
    }

    /// <summary>Folds a lower-authority record into the merged one.</summary>
    private static void Fold(CollectedBattery target, CollectedBattery candidate)
    {
        BatteryInfo info = target.Info, extra = candidate.Info;

        info.Name ??= extra.Name;
        info.Manufacturer ??= extra.Manufacturer;
        info.Model ??= extra.Model;
        info.SerialNumber ??= extra.SerialNumber;
        info.UniqueId ??= extra.UniqueId;
        info.ChemistryRaw ??= extra.ChemistryRaw;
        info.ManufactureDate ??= extra.ManufactureDate;
        target.UniqueId ??= candidate.UniqueId;

        if (info.Chemistry == BatteryChemistry.Unknown) info.Chemistry = extra.Chemistry;

        // The unit is NOT copied onto the target here. It is kept against the source that
        // declared it and resolved in Finalize against whichever source actually supplied
        // the capacity that won the merge (BUG-004).
        RecordCapacityUnit(target, candidate);

        // Only a source that read the capability bit may settle this; a source that said
        // nothing must not overwrite a positive or negative answer (BUG-001).
        info.IsSystemBattery ??= extra.IsSystemBattery;

        info.DesignCapacity = info.DesignCapacity.Or(extra.DesignCapacity);
        info.FullChargeCapacity = info.FullChargeCapacity.Or(extra.FullChargeCapacity);
        info.CycleCount = info.CycleCount.Or(extra.CycleCount);
        info.DesignVoltageMillivolts = info.DesignVoltageMillivolts.Or(extra.DesignVoltageMillivolts);

        foreach (DataSource source in extra.ContributingSources)
        {
            if (!info.ContributingSources.Contains(source)) info.ContributingSources.Add(source);
        }

        BatteryStatus status = target.Status, extraStatus = candidate.Status;

        if (status.ChargeState == ChargeState.Unknown) status.ChargeState = extraStatus.ChargeState;
        if (status.AcPower == AcPowerState.Unknown) status.AcPower = extraStatus.AcPower;
        status.IsCritical |= extraStatus.IsCritical;

        status.RemainingCapacity = status.RemainingCapacity.Or(extraStatus.RemainingCapacity);
        status.ChargePercent = status.ChargePercent.Or(extraStatus.ChargePercent);
        status.VoltageMillivolts = status.VoltageMillivolts.Or(extraStatus.VoltageMillivolts);
        status.RateMilliwatts = status.RateMilliwatts.Or(extraStatus.RateMilliwatts);
        status.CurrentMilliamps = status.CurrentMilliamps.Or(extraStatus.CurrentMilliamps);
        status.TemperatureKelvin = status.TemperatureKelvin.Or(extraStatus.TemperatureKelvin);
        status.EstimatedRuntimeSeconds = status.EstimatedRuntimeSeconds.Or(extraStatus.EstimatedRuntimeSeconds);
        status.RuntimeIsCalculating |= extraStatus.RuntimeIsCalculating;

        foreach (DataSource source in extraStatus.ContributingSources)
        {
            if (!status.ContributingSources.Contains(source)) status.ContributingSources.Add(source);
        }
    }

    /// <summary>
    /// Derives the values that follow from other readings, and applies the last of the
    /// SRS 20 sanity checks before anything reaches the UI.
    /// </summary>
    private static void Finalize(CollectedBattery battery, List<CollectionIssue> issues)
    {
        BatteryInfo info = battery.Info;
        BatteryStatus status = battery.Status;

        // Capacity readings must be positive to be meaningful.
        info.DesignCapacity = info.DesignCapacity.Where(v => v > 0, "Reported design capacity was not positive.");
        info.FullChargeCapacity = info.FullChargeCapacity.Where(
            v => v > 0, "Reported full-charge capacity was not positive.");
        status.RemainingCapacity = status.RemainingCapacity.Where(v => v >= 0);

        ResolveCapacityUnit(battery);

        // A per-battery percentage computed from this battery's own capacities is more
        // accurate than the whole-system estimate, so it supersedes it when available.
        if (status.RemainingCapacity.IsAvailable && info.FullChargeCapacity.IsAvailable
            && info.FullChargeCapacity.Value > 0)
        {
            double percent = (double)status.RemainingCapacity.Value / info.FullChargeCapacity.Value * 100.0;
            if (double.IsFinite(percent))
            {
                status.ChargePercent = Measured<double>.From(
                    Math.Clamp(percent, 0.0, 100.0),
                    DataSource.Calculated,
                    "Calculated from remaining and full-charge capacity.");
            }
        }

        status.ChargePercent = status.ChargePercent.Where(p => p is >= 0 and <= 100);

        // Runtime is only meaningful while running on battery (SRS 14).
        if (status.ChargeState is ChargeState.Charging or ChargeState.FullyCharged)
        {
            status.EstimatedRuntimeSeconds = Measured<int>.Unavailable();
            status.RuntimeIsCalculating = false;
        }
        else if (!status.EstimatedRuntimeSeconds.IsAvailable
                 && status.ChargeState == ChargeState.Discharging)
        {
            status.RuntimeIsCalculating = true;
        }

        ReconcileState(battery, issues);
    }

    /// <summary>
    /// Resolves the capacity unit against the source that actually supplied the capacity
    /// on display, rather than against whichever record anchored the merge (BUG-004).
    /// </summary>
    private static void ResolveCapacityUnit(CollectedBattery battery)
    {
        BatteryInfo info = battery.Info;

        // Design capacity is the figure the other two are compared against, so its source
        // settles the unit; the remaining readings fall back in order of significance.
        foreach (Measured<long> reading in new[]
                 {
                     info.DesignCapacity, info.FullChargeCapacity, battery.Status.RemainingCapacity,
                 })
        {
            if (!reading.IsAvailable) continue;
            if (battery.CapacityUnitBySource.TryGetValue(reading.Source, out CapacityUnit unit)
                && unit != CapacityUnit.Unknown)
            {
                info.CapacityUnit = unit;
                return;
            }
        }

        if (info.CapacityUnit != CapacityUnit.Unknown) return;

        // Every Windows source that reports an absolute capacity reports milliwatt-hours;
        // only a device advertising BATTERY_CAPACITY_RELATIVE differs, and the collectors
        // detect that explicitly.
        if (info.DesignCapacity.IsAvailable || info.FullChargeCapacity.IsAvailable)
        {
            info.CapacityUnit = CapacityUnit.MilliwattHours;
        }
    }

    /// <summary>
    /// Removes contradictions between the charge state and the mains state before either
    /// reaches the UI (SRS 8: the interface must never show contradictory information).
    ///
    /// Two physical facts drive this: the sign of the power flow is the ground truth for
    /// direction, and a battery cannot take charge without external power.
    /// </summary>
    private static void ReconcileState(CollectedBattery battery, List<CollectionIssue> issues)
    {
        BatteryStatus status = battery.Status;

        if (status.RateMilliwatts.IsAvailable && status.RateMilliwatts.Value != 0)
        {
            ChargeState fromRate = status.RateMilliwatts.Value > 0
                ? ChargeState.Charging
                : ChargeState.Discharging;

            // A measured direction of energy flow outranks any reported state, including
            // one a stronger source guessed wrong (BUG-005).
            status.ChargeState = fromRate;
        }

        if (status.ChargeState == ChargeState.Charging && status.AcPower == AcPowerState.Disconnected)
        {
            status.AcPower = AcPowerState.Connected;
            issues.Add(new CollectionIssue(
                DataSource.Calculated,
                "This battery reported that it is charging while also reporting that mains power "
                + "is disconnected. Charging requires external power, so it is shown as connected.",
                ElevationMayHelp: false));
        }

        if (status.ChargeState == ChargeState.FullyCharged && status.AcPower == AcPowerState.Disconnected)
        {
            // "Fully charged" is a mains-present state; on battery the pack is discharging.
            status.ChargeState = ChargeState.Discharging;
        }
    }
}
