using BatteryHealthChecker.Collectors;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>
/// Cross-source merging (SRS 4) and the derived values and sanity checks applied
/// before anything reaches the UI (SRS 20).
/// </summary>
public class BatteryRepositoryTests
{
    private static CollectedBattery Battery(
        DataSource source, int ordinal = 0, string? uniqueId = null, string? key = null)
    {
        var battery = new CollectedBattery { Ordinal = ordinal, UniqueId = uniqueId };
        battery.Info.DeviceKey = key ?? $"{source}-{ordinal}";
        battery.Info.Index = ordinal;
        battery.Info.UniqueId = uniqueId;
        battery.Info.CapacityUnit = CapacityUnit.MilliwattHours;
        battery.Info.ContributingSources.Add(source);
        battery.Status.DeviceKey = battery.Info.DeviceKey;
        battery.Status.ContributingSources.Add(source);
        return battery;
    }

    private static CollectionResult Result(params CollectedBattery[] batteries)
    {
        var result = new CollectionResult();
        result.Batteries.AddRange(batteries);
        return result;
    }

    [Fact]
    public void A_stronger_source_wins_for_a_field_both_sources_report()
    {
        CollectedBattery ioctl = Battery(DataSource.BatteryIoctl, uniqueId: "PACK-1");
        ioctl.Info.DesignCapacity = Measured<long>.From(60_000, DataSource.BatteryIoctl);

        CollectedBattery cim = Battery(DataSource.Win32Battery, uniqueId: "PACK-1");
        cim.Info.DesignCapacity = Measured<long>.From(11_111, DataSource.Win32Battery);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(ioctl), Result(cim) });

        Assert.Single(snapshot.Batteries);
        Assert.Equal(60_000, snapshot.Batteries[0].Info.DesignCapacity.Value);
    }

    [Fact]
    public void A_weaker_source_fills_a_gap_the_stronger_one_left()
    {
        CollectedBattery ioctl = Battery(DataSource.BatteryIoctl, uniqueId: "PACK-1");
        ioctl.Info.DesignCapacity = Measured<long>.From(60_000, DataSource.BatteryIoctl);

        // The common real-world case: the driver exposes capacity but not cycle count,
        // while the ACPI class does.
        CollectedBattery acpi = Battery(DataSource.WmiAcpi, uniqueId: "PACK-1");
        acpi.Info.CycleCount = Measured<int>.From(284, DataSource.WmiAcpi);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(ioctl), Result(acpi) });

        BatteryInfo merged = Assert.Single(snapshot.Batteries).Info;
        Assert.Equal(60_000, merged.DesignCapacity.Value);
        Assert.Equal(284, merged.CycleCount.Value);
    }

    [Fact]
    public void Charge_percent_is_derived_from_this_batterys_own_capacities()
    {
        CollectedBattery ioctl = Battery(DataSource.BatteryIoctl);
        ioctl.Info.DesignCapacity = Measured<long>.From(60_000, DataSource.BatteryIoctl);
        ioctl.Info.FullChargeCapacity = Measured<long>.From(50_000, DataSource.BatteryIoctl);
        ioctl.Status.RemainingCapacity = Measured<long>.From(25_000, DataSource.BatteryIoctl);
        // A whole-system estimate that disagrees must not win over the exact ratio.
        ioctl.Status.ChargePercent = Measured<double>.From(41, DataSource.SystemPowerStatus);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(ioctl) });

        BatteryStatus status = Assert.Single(snapshot.Batteries).Status;
        Assert.Equal(50.0, status.ChargePercent.Value, 6);
        Assert.Equal(DataSource.Calculated, status.ChargePercent.Source);
    }

    [Fact]
    public void Runtime_is_dropped_while_the_battery_is_charging()
    {
        CollectedBattery ioctl = Battery(DataSource.BatteryIoctl);
        ioctl.Status.ChargeState = ChargeState.Charging;
        ioctl.Status.EstimatedRuntimeSeconds = Measured<int>.From(3_600, DataSource.BatteryIoctl);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(ioctl) });

        BatteryStatus status = Assert.Single(snapshot.Batteries).Status;
        Assert.False(status.EstimatedRuntimeSeconds.IsAvailable);
        Assert.False(status.RuntimeIsCalculating);
    }

    [Fact]
    public void Discharging_without_an_estimate_is_reported_as_calculating()
    {
        CollectedBattery ioctl = Battery(DataSource.BatteryIoctl);
        ioctl.Status.ChargeState = ChargeState.Discharging;

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(ioctl) });

        Assert.True(Assert.Single(snapshot.Batteries).Status.RuntimeIsCalculating);
    }

    [Fact]
    public void A_whole_system_source_is_not_attributed_to_one_pack_when_several_exist()
    {
        CollectedBattery first = Battery(DataSource.BatteryIoctl, 0, "PACK-1");
        first.Info.DesignCapacity = Measured<long>.From(60_000, DataSource.BatteryIoctl);
        CollectedBattery second = Battery(DataSource.BatteryIoctl, 1, "PACK-2");
        second.Info.DesignCapacity = Measured<long>.From(30_000, DataSource.BatteryIoctl);

        CollectedBattery systemWide = Battery(DataSource.SystemPowerStatus);
        systemWide.Status.ChargePercent = Measured<double>.From(87, DataSource.SystemPowerStatus);

        BatterySnapshot snapshot = BatteryRepository.Merge(
            new[] { Result(first, second), Result(systemWide) });

        Assert.Equal(2, snapshot.Batteries.Count);
        // Neither pack may inherit the machine-wide figure as if it were its own.
        Assert.All(snapshot.Batteries, b => Assert.False(b.Status.ChargePercent.IsAvailable));
    }

    [Fact]
    public void A_whole_system_source_is_used_when_there_is_only_one_battery()
    {
        CollectedBattery only = Battery(DataSource.Win32Battery);
        CollectedBattery systemWide = Battery(DataSource.SystemPowerStatus);
        systemWide.Status.ChargePercent = Measured<double>.From(87, DataSource.SystemPowerStatus);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(only), Result(systemWide) });

        Assert.Equal(87.0, Assert.Single(snapshot.Batteries).Status.ChargePercent.Value, 6);
    }

    [Fact]
    public void Two_batteries_keep_separate_health_figures()
    {
        CollectedBattery first = Battery(DataSource.BatteryIoctl, 0, "PACK-1");
        first.Info.DesignCapacity = Measured<long>.From(60_000, DataSource.BatteryIoctl);
        first.Info.FullChargeCapacity = Measured<long>.From(55_200, DataSource.BatteryIoctl);

        CollectedBattery second = Battery(DataSource.BatteryIoctl, 1, "PACK-2");
        second.Info.DesignCapacity = Measured<long>.From(40_000, DataSource.BatteryIoctl);
        second.Info.FullChargeCapacity = Measured<long>.From(30_400, DataSource.BatteryIoctl);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(first, second) });

        Assert.Equal(2, snapshot.Batteries.Count);
        Assert.Equal(92.0, snapshot.Batteries[0].Health.HealthPercent, 6);
        Assert.Equal(76.0, snapshot.Batteries[1].Health.HealthPercent, 6);
        Assert.Equal(0, snapshot.Batteries[0].Info.Index);
        Assert.Equal(1, snapshot.Batteries[1].Info.Index);
    }

    [Fact]
    public void Non_positive_capacities_are_discarded_before_display()
    {
        CollectedBattery battery = Battery(DataSource.WmiAcpi);
        battery.Info.DesignCapacity = Measured<long>.From(0, DataSource.WmiAcpi);
        battery.Info.FullChargeCapacity = Measured<long>.From(-5, DataSource.WmiAcpi);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(battery) });

        BatteryReading reading = Assert.Single(snapshot.Batteries);
        Assert.False(reading.Info.DesignCapacity.IsAvailable);
        Assert.False(reading.Info.FullChargeCapacity.IsAvailable);
        Assert.False(reading.Health.IsAvailable);
    }

    [Fact]
    public void No_sources_with_batteries_yields_an_empty_snapshot_not_a_failure()
    {
        var empty = new CollectionResult { AcPower = AcPowerState.Connected };
        empty.Issues.Add(new CollectionIssue(DataSource.BatteryIoctl, "no device", false));

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { empty });

        Assert.False(snapshot.HasBattery);
        Assert.Equal(AcPowerState.Connected, snapshot.AcPower);
        Assert.Single(snapshot.Issues);
    }

    [Fact]
    public void Identical_issues_reported_by_several_sources_are_listed_once()
    {
        var first = new CollectionResult();
        first.Issues.Add(new CollectionIssue(DataSource.BatteryIoctl, "Access denied.", true));
        var second = new CollectionResult();
        second.Issues.Add(new CollectionIssue(DataSource.WmiAcpi, "Access denied.", true));

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { first, second });

        Assert.Single(snapshot.Issues);
    }
}
