using System.Management;
using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Collectors;

/// <summary>
/// Third-tier collector: the CIM classes <c>Win32_Battery</c> and
/// <c>Win32_PortableBattery</c> in <c>root\CIMV2</c> (SRS 4: WMI / CIM / Win32
/// battery information).
///
/// Win32_Battery leaves DesignCapacity and FullChargeCapacity null on nearly every
/// real laptop, so it is not trusted for health. It is still valuable for identity
/// (Win32_PortableBattery carries the SMBIOS manufacturer and model) and as a
/// charge-level source when the driver interfaces are unavailable.
/// </summary>
internal sealed class Win32BatteryCollector : IBatteryDataCollector
{
    private const string Scope = @"\\.\root\CIMV2";

    public DataSource Source => DataSource.Win32Battery;

    public string Name => "Win32_Battery / Win32_PortableBattery (CIM)";

    public bool SupportsStatusOnly => false;

    public CollectionResult Collect(bool statusOnly, CancellationToken cancellationToken)
    {
        var result = new CollectionResult();
        bool reportedError = false;

        void OnError(Exception ex)
        {
            if (reportedError) return;
            reportedError = true;
            result.Issues.Add(new CollectionIssue(
                Source,
                $"Win32_Battery could not be queried: {ex.Message}",
                ElevationMayHelp: ex is UnauthorizedAccessException));
        }

        var batteries = new List<CollectedBattery>();

        foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM Win32_Battery", OnError))
        {
            cancellationToken.ThrowIfCancellationRequested();
            batteries.Add(ReadBattery(obj, batteries.Count, result));
        }

        if (!statusOnly)
        {
            MergePortableBatteryIdentity(batteries, OnError, cancellationToken);
        }

        result.Batteries.AddRange(batteries);
        return result;
    }

    private CollectedBattery ReadBattery(ManagementBaseObject obj, int ordinal, CollectionResult result)
    {
        var battery = new CollectedBattery { Ordinal = ordinal };
        BatteryInfo info = battery.Info;
        BatteryStatus status = battery.Status;

        string key = WmiHelpers.GetString(obj, "DeviceID") ?? $"win32-battery-{ordinal}";
        info.DeviceKey = key;
        info.Index = ordinal;
        status.DeviceKey = key;
        battery.UniqueId = WmiHelpers.GetString(obj, "DeviceID");

        info.Name = WmiHelpers.GetString(obj, "Name", "Caption");
        info.Model = info.Name;
        info.ContributingSources.Add(Source);
        status.ContributingSources.Add(Source);

        int? chemistry = WmiHelpers.GetInt32(obj, "Chemistry");
        if (chemistry is { } c)
        {
            info.Chemistry = MapCimChemistry(c);
            info.ChemistryRaw ??= c.ToString();
        }

        // Present in the schema, virtually never populated - but when a machine does
        // populate them they are genuine mWh values, so take them.
        long? design = WmiHelpers.GetInt64(obj, "DesignCapacity");
        if (design is > 0) info.DesignCapacity = Measured<long>.From(design.Value, Source);

        long? full = WmiHelpers.GetInt64(obj, "FullChargeCapacity");
        if (full is > 0) info.FullChargeCapacity = Measured<long>.From(full.Value, Source);

        long? designVoltage = WmiHelpers.GetInt64(obj, "DesignVoltage");
        if (designVoltage is > 0 and <= int.MaxValue)
        {
            info.DesignVoltageMillivolts = Measured<int>.From((int)designVoltage.Value, Source);
        }

        if (info.CapacityUnit == CapacityUnit.Unknown) info.CapacityUnit = CapacityUnit.MilliwattHours;

        int? percent = WmiHelpers.GetInt32(obj, "EstimatedChargeRemaining");
        if (percent is >= 0 and <= 100)
        {
            status.ChargePercent = Measured<double>.From(percent.Value, Source);
        }

        int? batteryStatus = WmiHelpers.GetInt32(obj, "BatteryStatus");
        if (batteryStatus is { } bs)
        {
            (status.ChargeState, AcPowerState ac) = MapBatteryStatus(bs);
            if (ac != AcPowerState.Unknown)
            {
                status.AcPower = ac;
                if (result.AcPower == AcPowerState.Unknown) result.AcPower = ac;
            }
            status.IsCritical = bs is 5 or 9;
        }

        long? runtimeMinutes = WmiHelpers.GetInt64(obj, "EstimatedRunTime");
        // 71582788 minutes (0x04444444) is the documented sentinel Windows returns when
        // running on AC. Treating it as a real reading would show a 136-year runtime.
        if (runtimeMinutes is > 0 and < 7 * 24 * 60)
        {
            status.EstimatedRuntimeSeconds = Measured<int>.From((int)(runtimeMinutes.Value * 60), Source);
        }

        return battery;
    }

    /// <summary>
    /// Win32_PortableBattery comes from SMBIOS type 22 and is often the only place the
    /// pack's manufacturer and design capacity appear. Matched positionally because the
    /// two classes do not share a key.
    /// </summary>
    private void MergePortableBatteryIdentity(
        List<CollectedBattery> batteries, Action<Exception> onError, CancellationToken cancellationToken)
    {
        int index = 0;
        foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM Win32_PortableBattery", onError))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index >= batteries.Count) break;

            CollectedBattery battery = batteries[index++];
            BatteryInfo info = battery.Info;

            info.Manufacturer ??= WmiHelpers.GetString(obj, "Manufacturer");
            info.Model ??= WmiHelpers.GetString(obj, "Name", "Caption");
            info.Name ??= info.Model;

            if (!info.DesignCapacity.IsAvailable)
            {
                long? design = WmiHelpers.GetInt64(obj, "DesignCapacity");
                if (design is > 0) info.DesignCapacity = Measured<long>.From(design.Value, Source);
            }

            if (!info.DesignVoltageMillivolts.IsAvailable)
            {
                long? voltage = WmiHelpers.GetInt64(obj, "DesignVoltage");
                if (voltage is > 0 and <= int.MaxValue)
                {
                    info.DesignVoltageMillivolts = Measured<int>.From((int)voltage.Value, Source);
                }
            }

            if (info.Chemistry == BatteryChemistry.Unknown &&
                WmiHelpers.GetInt32(obj, "Chemistry") is { } chemistry)
            {
                info.Chemistry = MapCimChemistry(chemistry);
            }

            // SMBIOS ManufactureDate is a free-form string; only accept a parseable date.
            string? manufactured = WmiHelpers.GetString(obj, "ManufactureDate");
            if (info.ManufactureDate is null && manufactured is not null &&
                DateTime.TryParse(manufactured, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime parsed))
            {
                info.ManufactureDate = parsed;
            }
        }
    }

    /// <summary>Maps Win32_Battery.BatteryStatus to a charge state and, where implied, AC state.</summary>
    internal static (ChargeState State, AcPowerState Ac) MapBatteryStatus(int value) => value switch
    {
        1 => (ChargeState.Discharging, AcPowerState.Disconnected),
        // 2 is documented as "the system has access to AC, so the battery is not
        // discharging" - it says nothing about whether charging is under way.
        2 => (ChargeState.NotCharging, AcPowerState.Connected),
        3 => (ChargeState.FullyCharged, AcPowerState.Unknown),
        4 or 5 => (ChargeState.Discharging, AcPowerState.Disconnected),
        6 or 7 or 8 or 9 => (ChargeState.Charging, AcPowerState.Connected),
        11 => (ChargeState.NotCharging, AcPowerState.Unknown),
        _ => (ChargeState.Unknown, AcPowerState.Unknown),
    };

    /// <summary>Maps the CIM Chemistry enumeration.</summary>
    internal static BatteryChemistry MapCimChemistry(int value) => value switch
    {
        3 => BatteryChemistry.LeadAcid,
        4 => BatteryChemistry.NickelCadmium,
        5 => BatteryChemistry.NickelMetalHydride,
        6 => BatteryChemistry.LithiumIon,
        7 => BatteryChemistry.ZincAir,
        8 => BatteryChemistry.LithiumPolymer,
        1 => BatteryChemistry.Other,
        _ => BatteryChemistry.Unknown,
    };
}
