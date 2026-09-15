using BatteryHealthChecker.Models;
using BatteryHealthChecker.Native;
using static BatteryHealthChecker.Native.BatteryNative;

namespace BatteryHealthChecker.Collectors;

/// <summary>
/// Primary collector: talks to the battery miniport driver through
/// IOCTL_BATTERY_QUERY_INFORMATION / IOCTL_BATTERY_QUERY_STATUS (SRS 4).
///
/// This is the only source that reliably exposes designed capacity, full-charged
/// capacity and cycle count on real laptops - Win32_Battery leaves those null on
/// almost every machine - so it sits at the top of the fallback chain.
/// </summary>
internal sealed class IoctlBatteryCollector : IBatteryDataCollector
{
    public DataSource Source => DataSource.BatteryIoctl;

    public string Name => "Battery device interface (IOCTL)";

    public bool SupportsStatusOnly => true;

    public CollectionResult Collect(bool statusOnly, CancellationToken cancellationToken)
    {
        var result = new CollectionResult();
        IReadOnlyList<BatteryDevice> devices;

        try
        {
            devices = BatteryDevice.OpenAll((path, error) => result.Issues.Add(new CollectionIssue(
                Source,
                $"Unable to access a battery device ({Describe(error)}).",
                ElevationMayHelp: error == ERROR_ACCESS_DENIED)));
        }
        catch (BatteryDeviceException ex)
        {
            result.Issues.Add(new CollectionIssue(Source, ex.Message, ex.ElevationMayHelp));
            return result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Defensive: an environment without setupapi is not a supported Windows
            // desktop, but it must degrade to the next collector rather than crash.
            result.Issues.Add(new CollectionIssue(
                Source, "The Windows battery device interface is not available on this system.", false));
            return result;
        }

        try
        {
            int ordinal = 0;
            foreach (BatteryDevice device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    CollectedBattery? battery = Read(device, ordinal, statusOnly);
                    if (battery is not null)
                    {
                        result.Batteries.Add(battery);
                        ordinal++;
                    }
                }
                catch (BatteryDeviceException ex)
                {
                    result.Issues.Add(new CollectionIssue(Source, ex.Message, ex.ElevationMayHelp));
                }
                catch (Exception ex)
                {
                    result.Issues.Add(new CollectionIssue(
                        Source, $"A battery query failed: {ex.Message}", false));
                }
            }
        }
        finally
        {
            foreach (BatteryDevice device in devices) device.Dispose();
        }

        return result;
    }

    private CollectedBattery? Read(BatteryDevice device, int ordinal, bool statusOnly)
    {
        var battery = new CollectedBattery { Ordinal = ordinal };
        BatteryInfo info = battery.Info;
        BatteryStatus status = battery.Status;

        info.DeviceKey = device.DevicePath;
        info.Index = ordinal;

        BATTERY_INFORMATION? raw = device.QueryInformation<BATTERY_INFORMATION>(
            BatteryQueryInformationLevel.BatteryInformation);

        if (raw is { } bi)
        {
            bool relative = (bi.Capabilities & BATTERY_CAPACITY_RELATIVE) != 0;
            info.CapacityUnit = relative ? CapacityUnit.Relative : CapacityUnit.MilliwattHours;
            info.IsSystemBattery = (bi.Capabilities & BATTERY_SYSTEM_BATTERY) != 0;

            info.DesignCapacity = Capacity(bi.DesignedCapacity);
            info.FullChargeCapacity = Capacity(bi.FullChargedCapacity);

            // A cycle count of 0 is how the great majority of drivers say "not tracked".
            // Reporting it as a real zero would be a fabricated value (SRS 13).
            info.CycleCount = bi.CycleCount > 0
                ? Measured<int>.From((int)Math.Min(bi.CycleCount, int.MaxValue), Source)
                : Measured<int>.Unavailable("The battery firmware does not report a cycle count.");

            string chemistry = new(new[]
            {
                (char)bi.Chemistry0, (char)bi.Chemistry1, (char)bi.Chemistry2, (char)bi.Chemistry3,
            });
            chemistry = chemistry.Trim('\0', ' ');
            if (chemistry.Length > 0)
            {
                info.ChemistryRaw = chemistry;
                info.Chemistry = MapChemistry(chemistry);
            }

            info.ContributingSources.Add(Source);
        }

        if (!statusOnly)
        {
            info.Name = device.QueryString(BatteryQueryInformationLevel.BatteryDeviceName) ?? info.Name;
            info.Manufacturer = device.QueryString(BatteryQueryInformationLevel.BatteryManufactureName);
            info.SerialNumber = device.QueryString(BatteryQueryInformationLevel.BatterySerialNumber);
            info.UniqueId = device.QueryString(BatteryQueryInformationLevel.BatteryUniqueID);
            battery.UniqueId = info.UniqueId;

            // The device name is the closest thing the interface exposes to a model.
            info.Model = info.Name;

            BATTERY_MANUFACTURE_DATE? date = device.QueryInformation<BATTERY_MANUFACTURE_DATE>(
                BatteryQueryInformationLevel.BatteryManufactureDate);
            if (date is { } d && d.Year is > 1980 and < 2200 && d.Month is >= 1 and <= 12 && d.Day is >= 1 and <= 31)
            {
                try
                {
                    info.ManufactureDate = new DateTime(d.Year, d.Month, d.Day);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Firmware reported an impossible date; drop it rather than show it.
                }
            }
        }

        ReadStatus(device, status);
        ReadTemperature(device, status);
        ReadEstimatedRuntime(device, status);

        status.DeviceKey = info.DeviceKey;
        status.ContributingSources.Add(Source);

        // A device that answered nothing useful is worse than no entry at all - it
        // would render as a row of "Not Available" and hide a working fallback source.
        bool anythingUseful = raw is not null
            || status.RemainingCapacity.IsAvailable
            || status.ChargeState != ChargeState.Unknown;

        return anythingUseful ? battery : null;
    }

    private void ReadStatus(BatteryDevice device, BatteryStatus status)
    {
        BATTERY_STATUS? s = device.QueryStatus();
        if (s is not { } bs) return;

        bool charging = (bs.PowerState & BATTERY_CHARGING) != 0;
        bool discharging = (bs.PowerState & BATTERY_DISCHARGING) != 0;
        bool online = (bs.PowerState & BATTERY_POWER_ON_LINE) != 0;

        status.AcPower = online ? AcPowerState.Connected : AcPowerState.Disconnected;
        status.IsCritical = (bs.PowerState & BATTERY_CRITICAL) != 0;

        status.ChargeState = (charging, discharging, online) switch
        {
            (true, _, _) => ChargeState.Charging,
            (false, true, _) => ChargeState.Discharging,
            // On AC, not charging and not discharging: the pack is holding station,
            // which is what "fully charged" looks like to the driver.
            (false, false, true) => ChargeState.FullyCharged,
            _ => ChargeState.NotCharging,
        };

        status.RemainingCapacity = Capacity(bs.Capacity);

        status.VoltageMillivolts = bs.Voltage != BATTERY_UNKNOWN_VOLTAGE && bs.Voltage > 0
            ? Measured<int>.From((int)Math.Min(bs.Voltage, int.MaxValue), Source)
            : Measured<int>.Unavailable();

        if (bs.Rate != BATTERY_UNKNOWN_RATE)
        {
            status.RateMilliwatts = Measured<int>.From(bs.Rate, Source);

            // P = V x I, so I(mA) = P(mW) / V(V) = rate * 1000 / voltage(mV).
            if (status.VoltageMillivolts.IsAvailable && status.VoltageMillivolts.Value > 0)
            {
                double milliamps = bs.Rate * 1000.0 / status.VoltageMillivolts.Value;
                if (double.IsFinite(milliamps))
                {
                    status.CurrentMilliamps = Measured<double>.From(
                        milliamps, DataSource.Calculated, "Derived from power flow and voltage.");
                }
            }
        }
    }

    private void ReadTemperature(BatteryDevice device, BatteryStatus status)
    {
        uint? tenthsKelvin = device.QueryUInt32(BatteryQueryInformationLevel.BatteryTemperature);
        if (tenthsKelvin is not { } t || t == 0) return;

        double kelvin = t / 10.0;

        // Guard against firmware that answers the level with a placeholder: anything
        // outside roughly -60 C to +150 C is not a believable battery temperature.
        if (kelvin is < 213 or > 423) return;

        status.TemperatureKelvin = Measured<double>.From(kelvin, Source);
    }

    private void ReadEstimatedRuntime(BatteryDevice device, BatteryStatus status)
    {
        // AtRate = 0 asks the driver to use the present discharge rate. While charging
        // or on AC the driver correctly answers BATTERY_UNKNOWN_TIME.
        uint? seconds = device.QueryUInt32(BatteryQueryInformationLevel.BatteryEstimatedTime);
        if (seconds is not { } s || s == BATTERY_UNKNOWN_TIME) return;

        // A week of runtime is not a real reading from a laptop pack.
        if (s > 7 * 24 * 3600) return;

        status.EstimatedRuntimeSeconds = Measured<int>.From((int)s, Source);
    }

    private Measured<long> Capacity(uint value) =>
        value != BATTERY_UNKNOWN_CAPACITY
            ? Measured<long>.From(value, Source)
            : Measured<long>.Unavailable();

    /// <summary>Maps the four-character ACPI chemistry code. Unrecognised codes stay Other.</summary>
    internal static BatteryChemistry MapChemistry(string code) => code.ToUpperInvariant() switch
    {
        "LION" or "LI-I" => BatteryChemistry.LithiumIon,
        "LIP" or "LIPO" or "LI-P" => BatteryChemistry.LithiumPolymer,
        "NIMH" => BatteryChemistry.NickelMetalHydride,
        "NICD" => BatteryChemistry.NickelCadmium,
        "NIZN" => BatteryChemistry.NickelZinc,
        "PBAC" => BatteryChemistry.LeadAcid,
        "RAM" => BatteryChemistry.RechargeableAlkalineManganese,
        _ => BatteryChemistry.Other,
    };

    private static string Describe(int win32Error) => win32Error switch
    {
        ERROR_ACCESS_DENIED => "access denied",
        ERROR_FILE_NOT_FOUND => "device not found",
        ERROR_NOT_SUPPORTED => "not supported by the driver",
        _ => $"Windows error {win32Error}",
    };
}
