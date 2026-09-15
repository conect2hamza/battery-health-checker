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
                Issues = distinctIssues,
            };
        }

        CollectionResult anchor = withBatteries[0];
        // The anchor's records are mutated in place as lower-authority sources are folded
        // in. That is safe because every collector builds fresh objects for each scan,
        // so nothing outside this merge holds a reference to them.
        List<CollectedBattery> merged = anchor.Batteries.OrderBy(b => b.Ordinal).ToList();

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

        var readings = new List<BatteryReading>(merged.Count);
        for (int i = 0; i < merged.Count; i++)
        {
            CollectedBattery battery = merged[i];
            battery.Info.Index = i;
            battery.Status.DeviceKey = battery.Info.DeviceKey;

            Finalize(battery);

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
            Issues = distinctIssues,
        };
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
        if (info.CapacityUnit == CapacityUnit.Unknown) info.CapacityUnit = extra.CapacityUnit;

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
    private static void Finalize(CollectedBattery battery)
    {
        BatteryInfo info = battery.Info;
        BatteryStatus status = battery.Status;

        // Capacity readings must be positive to be meaningful.
        info.DesignCapacity = info.DesignCapacity.Where(v => v > 0, "Reported design capacity was not positive.");
        info.FullChargeCapacity = info.FullChargeCapacity.Where(
            v => v > 0, "Reported full-charge capacity was not positive.");
        status.RemainingCapacity = status.RemainingCapacity.Where(v => v >= 0);

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

        if (status.RateMilliwatts.IsAvailable)
        {
            // The sign of the rate is the authoritative direction of energy flow; use it
            // to correct a state that a weaker source may have guessed wrong.
            int rate = status.RateMilliwatts.Value;
            if (rate > 0 && status.ChargeState == ChargeState.Unknown) status.ChargeState = ChargeState.Charging;
            if (rate < 0 && status.ChargeState == ChargeState.Unknown) status.ChargeState = ChargeState.Discharging;
        }

        if (info.CapacityUnit == CapacityUnit.Unknown && info.DesignCapacity.IsAvailable)
        {
            // Every Windows source that reports an absolute capacity reports milliwatt-hours;
            // only a device advertising BATTERY_CAPACITY_RELATIVE differs, and that is
            // detected explicitly by the collectors.
            info.CapacityUnit = CapacityUnit.MilliwattHours;
        }
    }
}
