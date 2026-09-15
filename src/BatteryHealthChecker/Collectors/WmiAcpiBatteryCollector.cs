using System.Management;
using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Collectors;

/// <summary>
/// Secondary collector: the ACPI-backed classes in the <c>root\WMI</c> namespace
/// (SRS 4: WMI / CIM / ACPI-exposed information).
///
/// These are published by the same battery miniport as the IOCTL interface, but some
/// vendors populate one and not the other - most often cycle count and temperature.
/// Running both and merging is exactly the fallback behaviour SRS 4 asks for.
///
/// Every class here is keyed by InstanceName, which is what correlates the several
/// per-topic classes back to one physical battery.
/// </summary>
internal sealed class WmiAcpiBatteryCollector : IBatteryDataCollector
{
    private const string Scope = @"\\.\root\WMI";

    public DataSource Source => DataSource.WmiAcpi;

    public string Name => @"ACPI battery classes (root\WMI)";

    public bool SupportsStatusOnly => true;

    public CollectionResult Collect(bool statusOnly, CancellationToken cancellationToken)
    {
        var result = new CollectionResult();
        var byInstance = new Dictionary<string, CollectedBattery>(StringComparer.OrdinalIgnoreCase);
        bool reportedError = false;

        void OnError(Exception ex)
        {
            if (reportedError) return;
            reportedError = true;
            result.Issues.Add(new CollectionIssue(
                Source,
                $"The ACPI battery classes could not be queried: {ex.Message}",
                ElevationMayHelp: ex is UnauthorizedAccessException));
        }

        CollectedBattery Lookup(ManagementBaseObject obj)
        {
            string instance = WmiHelpers.GetString(obj, "InstanceName") ?? $"acpi-{byInstance.Count}";
            if (!byInstance.TryGetValue(instance, out CollectedBattery? battery))
            {
                battery = new CollectedBattery { Ordinal = byInstance.Count };
                battery.Info.DeviceKey = instance;
                battery.Info.Index = battery.Ordinal;
                battery.Status.DeviceKey = instance;
                battery.Info.ContributingSources.Add(Source);
                battery.Status.ContributingSources.Add(Source);
                byInstance[instance] = battery;
            }
            return battery;
        }

        if (!statusOnly)
        {
            foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM BatteryStaticData", OnError))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadStaticData(Lookup(obj), obj);
            }

            foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM BatteryCycleCount", OnError))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int? cycles = WmiHelpers.GetInt32(obj, "CycleCount");
                // As with the IOCTL path, 0 means "not tracked", not "brand new" (SRS 13).
                if (cycles is > 0)
                {
                    Lookup(obj).Info.CycleCount = Measured<int>.From(cycles.Value, Source);
                }
            }
        }

        foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM BatteryFullChargedCapacity", OnError))
        {
            cancellationToken.ThrowIfCancellationRequested();
            long? full = WmiHelpers.GetInt64(obj, "FullChargedCapacity");
            if (full is > 0)
            {
                Lookup(obj).Info.FullChargeCapacity = Measured<long>.From(full.Value, Source);
            }
        }

        foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM BatteryStatus", OnError))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadStatus(Lookup(obj), obj, result);
        }

        foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM BatteryRuntime", OnError))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? seconds = WmiHelpers.GetInt32(obj, "EstimatedRuntime", "Runtime");
            if (seconds is > 0 and <= 7 * 24 * 3600)
            {
                Lookup(obj).Status.EstimatedRuntimeSeconds = Measured<int>.From(seconds.Value, Source);
            }
        }

        foreach (ManagementBaseObject obj in WmiHelpers.Query(Scope, "SELECT * FROM BatteryTemperature", OnError))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Reported in tenths of a degree Kelvin, same unit as the IOCTL level.
            long? tenths = WmiHelpers.GetInt64(obj, "Temperature");
            if (tenths is > 0)
            {
                double kelvin = tenths.Value / 10.0;
                if (kelvin is >= 213 and <= 423)
                {
                    Lookup(obj).Status.TemperatureKelvin = Measured<double>.From(kelvin, Source);
                }
            }
        }

        result.Batteries.AddRange(byInstance.Values.OrderBy(b => b.Ordinal));
        return result;
    }

    private void ReadStaticData(CollectedBattery battery, ManagementBaseObject obj)
    {
        BatteryInfo info = battery.Info;

        info.UniqueId = WmiHelpers.GetString(obj, "UniqueID", "UniqueId");
        battery.UniqueId = info.UniqueId;
        info.Name = WmiHelpers.GetString(obj, "DeviceName") ?? info.Name;
        info.Model = info.Name;
        info.Manufacturer = WmiHelpers.GetString(obj, "ManufactureName", "ManufacturerName");
        info.SerialNumber = WmiHelpers.GetString(obj, "SerialNumber");

        long? design = WmiHelpers.GetInt64(obj, "DesignedCapacity", "DesignCapacity");
        if (design is > 0)
        {
            info.DesignCapacity = Measured<long>.From(design.Value, Source);
        }

        long? capabilities = WmiHelpers.GetInt64(obj, "Capabilities");
        if (capabilities is { } caps)
        {
            bool relative = (caps & Native.BatteryNative.BATTERY_CAPACITY_RELATIVE) != 0;
            info.CapacityUnit = relative ? CapacityUnit.Relative : CapacityUnit.MilliwattHours;
            info.IsSystemBattery = (caps & Native.BatteryNative.BATTERY_SYSTEM_BATTERY) != 0;
        }
        else if (info.CapacityUnit == CapacityUnit.Unknown)
        {
            info.CapacityUnit = CapacityUnit.MilliwattHours;
        }

        byte[]? chemistry = WmiHelpers.GetBytes(obj, "Chemistry");
        if (chemistry is { Length: > 0 })
        {
            string code = new string(chemistry.Select(b => (char)b).ToArray()).Trim('\0', ' ');
            if (code.Length > 0)
            {
                info.ChemistryRaw = code;
                info.Chemistry = IoctlBatteryCollector.MapChemistry(code);
            }
        }
    }

    private void ReadStatus(CollectedBattery battery, ManagementBaseObject obj, CollectionResult result)
    {
        BatteryStatus status = battery.Status;

        bool charging = WmiHelpers.GetBool(obj, "Charging") ?? false;
        bool discharging = WmiHelpers.GetBool(obj, "Discharging") ?? false;
        bool? online = WmiHelpers.GetBool(obj, "PowerOnline");

        if (online is { } ac)
        {
            status.AcPower = ac ? AcPowerState.Connected : AcPowerState.Disconnected;
            if (result.AcPower == AcPowerState.Unknown) result.AcPower = status.AcPower;
        }

        status.IsCritical = WmiHelpers.GetBool(obj, "Critical") ?? false;
        status.ChargeState = (charging, discharging, online) switch
        {
            (true, _, _) => ChargeState.Charging,
            (false, true, _) => ChargeState.Discharging,
            (false, false, true) => ChargeState.FullyCharged,
            _ => ChargeState.NotCharging,
        };

        long? remaining = WmiHelpers.GetInt64(obj, "RemainingCapacity", "Capacity");
        if (remaining is >= 0)
        {
            status.RemainingCapacity = Measured<long>.From(remaining.Value, Source);
        }

        int? voltage = WmiHelpers.GetInt32(obj, "Voltage");
        if (voltage is > 0)
        {
            status.VoltageMillivolts = Measured<int>.From(voltage.Value, Source);
        }

        // Some schemas expose a single signed Rate, others a pair of unsigned rates.
        int? rate = WmiHelpers.GetInt32(obj, "Rate");
        if (rate is null)
        {
            int? chargeRate = WmiHelpers.GetInt32(obj, "ChargeRate");
            int? dischargeRate = WmiHelpers.GetInt32(obj, "DischargeRate");
            if (chargeRate is > 0) rate = chargeRate;
            else if (dischargeRate is > 0) rate = -dischargeRate;
        }

        if (rate is { } r && r != Native.BatteryNative.BATTERY_UNKNOWN_RATE)
        {
            status.RateMilliwatts = Measured<int>.From(r, Source);
            if (status.VoltageMillivolts.IsAvailable && status.VoltageMillivolts.Value > 0)
            {
                double milliamps = r * 1000.0 / status.VoltageMillivolts.Value;
                if (double.IsFinite(milliamps))
                {
                    status.CurrentMilliamps = Measured<double>.From(
                        milliamps, DataSource.Calculated, "Derived from power flow and voltage.");
                }
            }
        }
    }
}
