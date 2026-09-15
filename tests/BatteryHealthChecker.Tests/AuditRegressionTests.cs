using BatteryHealthChecker.Collectors;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>
/// One test per defect found in the 1.0.1 audit, each reproducing the exact probe that
/// exposed it. These are the regression guards for docs/audit.md.
/// </summary>
public class AuditRegressionTests
{
    private static CollectedBattery Battery(
        DataSource source, int ordinal = 0, string? uniqueId = null, bool? systemBattery = null)
    {
        var battery = new CollectedBattery { Ordinal = ordinal, UniqueId = uniqueId };
        battery.Info.DeviceKey = $"{source}-{ordinal}";
        battery.Info.Index = ordinal;
        battery.Info.UniqueId = uniqueId;
        battery.Info.IsSystemBattery = systemBattery;
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

    private static Measured<long> Capacity(long value, DataSource source) =>
        Measured<long>.From(value, source);

    // ---------------------------------------------------------------- BUG-001

    [Fact]
    public void A_peripheral_battery_never_leads_the_list()
    {
        // Probe D: a UPS enumerated first must not become "Battery 1" and drive the
        // health headline ahead of the machine's own pack.
        CollectedBattery ups = Battery(DataSource.BatteryIoctl, 0, "UPS", systemBattery: false);
        ups.Info.Name = "APC Back-UPS";
        ups.Info.DesignCapacity = Capacity(12_000, DataSource.BatteryIoctl);
        ups.Info.FullChargeCapacity = Capacity(6_000, DataSource.BatteryIoctl);

        CollectedBattery laptop = Battery(DataSource.BatteryIoctl, 1, "P1", systemBattery: true);
        laptop.Info.Name = "Laptop pack";
        laptop.Info.DesignCapacity = Capacity(60_000, DataSource.BatteryIoctl);
        laptop.Info.FullChargeCapacity = Capacity(57_000, DataSource.BatteryIoctl);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(ups, laptop) });

        Assert.Equal("Laptop pack", snapshot.Batteries[0].Info.Name);
        Assert.Equal(95.0, snapshot.Batteries[0].Health.HealthPercent, 6);
        Assert.True(snapshot.Batteries[0].Info.IsConfirmedSystemBattery);
        Assert.True(snapshot.Batteries[1].Info.IsConfirmedPeripheral);
    }

    [Fact]
    public void A_battery_whose_capability_was_never_reported_is_not_called_a_peripheral()
    {
        // Win32_Battery and GetSystemPowerStatus do not expose the capability bit at all.
        // Silence must not be read as "this is not the system battery".
        CollectedBattery unknown = Battery(DataSource.Win32Battery);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(unknown) });

        BatteryInfo info = Assert.Single(snapshot.Batteries).Info;
        Assert.Null(info.IsSystemBattery);
        Assert.False(info.IsConfirmedPeripheral);
        Assert.True(snapshot.HasSystemBattery);
    }

    [Fact]
    public void A_source_that_reports_nothing_cannot_overwrite_a_known_capability()
    {
        CollectedBattery ioctl = Battery(DataSource.BatteryIoctl, 0, "P1", systemBattery: false);
        CollectedBattery cim = Battery(DataSource.Win32Battery, 0, "P1", systemBattery: null);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(ioctl), Result(cim) });

        Assert.False(Assert.Single(snapshot.Batteries).Info.IsSystemBattery);
    }

    // ---------------------------------------------------------------- BUG-002

    [Fact]
    public void A_phantom_device_is_rejected_when_Windows_reports_no_system_battery()
    {
        // Probe F: an ACPI entry survives while GetSystemPowerStatus positively reports
        // BATTERY_FLAG_NO_BATTERY. The authoritative signal wins.
        CollectedBattery phantom = Battery(DataSource.WmiAcpi);
        phantom.Info.Name = "Phantom";

        var powerStatus = new CollectionResult
        {
            AcPower = AcPowerState.Connected,
            ReportedNoBattery = true,
        };

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(phantom), powerStatus });

        Assert.False(snapshot.HasBattery);
        Assert.True(snapshot.SystemReportsNoBattery);
        // The rejection is explained rather than silent.
        Assert.Contains(snapshot.Issues, i => i.Message.Contains("no system battery"));
    }

    [Fact]
    public void A_confirmed_system_battery_survives_a_contradictory_no_battery_flag()
    {
        // The driver interface outranks the coarse power API: if the miniport tagged a
        // system battery, a stale BATTERY_FLAG_NO_BATTERY must not erase it.
        CollectedBattery real = Battery(DataSource.BatteryIoctl, 0, "P1", systemBattery: true);
        real.Info.DesignCapacity = Capacity(60_000, DataSource.BatteryIoctl);

        var powerStatus = new CollectionResult { ReportedNoBattery = true };

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(real), powerStatus });

        Assert.True(snapshot.HasBattery);
        Assert.Equal(60_000, Assert.Single(snapshot.Batteries).Info.DesignCapacity.Value);
    }

    // ---------------------------------------------------------------- BUG-004

    [Fact]
    public void A_milliwatt_hour_reading_is_never_labelled_with_another_sources_unit()
    {
        // Probe A: the IOCTL device advertises BATTERY_CAPACITY_RELATIVE but supplies no
        // capacity; the ACPI class supplies a genuine 60,000 mWh. The unit must follow
        // the value, not the record that anchored the merge.
        CollectedBattery relative = Battery(DataSource.BatteryIoctl, 0, "P1");
        relative.Info.CapacityUnit = CapacityUnit.Relative;

        CollectedBattery acpi = Battery(DataSource.WmiAcpi, 0, "P1");
        acpi.Info.CapacityUnit = CapacityUnit.MilliwattHours;
        acpi.Info.DesignCapacity = Capacity(60_000, DataSource.WmiAcpi);
        acpi.Info.FullChargeCapacity = Capacity(52_200, DataSource.WmiAcpi);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(relative), Result(acpi) });

        BatteryInfo info = Assert.Single(snapshot.Batteries).Info;
        Assert.Equal(CapacityUnit.MilliwattHours, info.CapacityUnit);
        Assert.Equal("60,000 mWh", BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit));
    }

    [Fact]
    public void A_genuinely_relative_device_is_still_never_labelled_mWh()
    {
        // The inverse of the bug: a device that really does report unitless values must
        // keep saying so, or the fix would trade one fabricated unit for another.
        CollectedBattery relative = Battery(DataSource.BatteryIoctl, 0, "P1");
        relative.Info.CapacityUnit = CapacityUnit.Relative;
        relative.Info.DesignCapacity = Capacity(100, DataSource.BatteryIoctl);
        relative.Info.FullChargeCapacity = Capacity(87, DataSource.BatteryIoctl);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(relative) });

        BatteryInfo info = Assert.Single(snapshot.Batteries).Info;
        Assert.Equal(CapacityUnit.Relative, info.CapacityUnit);
        Assert.DoesNotContain("mWh", BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit));
        // The ratio is still meaningful even without a unit.
        Assert.Equal(87.0, Assert.Single(snapshot.Batteries).Health.HealthPercent, 6);
    }

    // ---------------------------------------------------------------- BUG-005

    [Fact]
    public void Charging_while_mains_is_reported_disconnected_is_reconciled()
    {
        // Probe E: SRS 8 forbids showing contradictory information. A pack cannot take
        // charge without external power.
        CollectedBattery battery = Battery(DataSource.BatteryIoctl);
        battery.Status.ChargeState = ChargeState.Charging;
        battery.Status.AcPower = AcPowerState.Disconnected;

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(battery) });

        BatteryStatus status = Assert.Single(snapshot.Batteries).Status;
        Assert.Equal(ChargeState.Charging, status.ChargeState);
        Assert.Equal(AcPowerState.Connected, status.AcPower);
        Assert.Contains(snapshot.Issues, i => i.Message.Contains("Charging requires external power"));
    }

    [Fact]
    public void A_measured_discharge_rate_overrides_a_state_that_says_charging()
    {
        CollectedBattery battery = Battery(DataSource.BatteryIoctl);
        battery.Status.ChargeState = ChargeState.Charging;
        battery.Status.RateMilliwatts = Measured<int>.From(-12_400, DataSource.BatteryIoctl);

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(battery) });

        Assert.Equal(ChargeState.Discharging, Assert.Single(snapshot.Batteries).Status.ChargeState);
    }

    [Fact]
    public void Fully_charged_on_battery_power_is_reported_as_discharging()
    {
        CollectedBattery battery = Battery(DataSource.BatteryIoctl);
        battery.Status.ChargeState = ChargeState.FullyCharged;
        battery.Status.AcPower = AcPowerState.Disconnected;

        BatterySnapshot snapshot = BatteryRepository.Merge(new[] { Result(battery) });

        BatteryStatus status = Assert.Single(snapshot.Batteries).Status;
        Assert.Equal(ChargeState.Discharging, status.ChargeState);
        Assert.Equal(AcPowerState.Disconnected, status.AcPower);
    }

    // ---------------------------------------------------------------- BUG-006

    [Fact]
    public void A_negative_capacity_is_reported_as_invalid_not_as_missing()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(
            Capacity(50_000, DataSource.BatteryIoctl), Capacity(-1_000, DataSource.BatteryIoctl));

        Assert.False(result.IsAvailable);
        Assert.Equal(HealthUnavailableReason.InvalidFullChargeCapacity, result.Reason);
    }
}
