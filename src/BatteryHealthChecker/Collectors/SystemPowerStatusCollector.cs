using System.Runtime.InteropServices;
using BatteryHealthChecker.Models;
using static BatteryHealthChecker.Native.BatteryNative;

namespace BatteryHealthChecker.Collectors;

/// <summary>
/// Floor-level collector: <c>GetSystemPowerStatus</c> (SRS 4).
///
/// It is coarse - a whole-system charge percentage, an AC flag and a runtime estimate -
/// but it is the one source that answers on every Windows machine without a driver,
/// a namespace or a privilege. It is also the authority for "this machine has no
/// battery at all", which is what drives the desktop message in SRS 19.
/// </summary>
internal sealed class SystemPowerStatusCollector : IBatteryDataCollector
{
    public DataSource Source => DataSource.SystemPowerStatus;

    public string Name => "GetSystemPowerStatus (Windows power API)";

    public bool SupportsStatusOnly => true;

    public CollectionResult Collect(bool statusOnly, CancellationToken cancellationToken)
    {
        var result = new CollectionResult();

        SYSTEM_POWER_STATUS power;
        try
        {
            if (!GetSystemPowerStatus(out power))
            {
                result.Issues.Add(new CollectionIssue(
                    Source,
                    $"Windows could not report system power status (error {Marshal.GetLastWin32Error()}).",
                    false));
                return result;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            result.Issues.Add(new CollectionIssue(
                Source, "The Windows power API is not available on this system.", false));
            return result;
        }

        result.AcPower = power.ACLineStatus switch
        {
            AC_LINE_ONLINE => AcPowerState.Connected,
            AC_LINE_OFFLINE => AcPowerState.Disconnected,
            _ => AcPowerState.Unknown,
        };

        if (power.BatteryFlag != BATTERY_FLAG_UNKNOWN && (power.BatteryFlag & BATTERY_FLAG_NO_BATTERY) != 0)
        {
            result.ReportedNoBattery = true;
            return result;
        }

        var battery = new CollectedBattery { Ordinal = 0 };
        battery.Info.DeviceKey = "system-power-status";
        battery.Info.Index = 0;
        battery.Info.ContributingSources.Add(Source);
        battery.Status.DeviceKey = battery.Info.DeviceKey;
        battery.Status.AcPower = result.AcPower;
        battery.Status.ContributingSources.Add(Source);

        if (power.BatteryLifePercent != BATTERY_PERCENTAGE_UNKNOWN && power.BatteryLifePercent <= 100)
        {
            battery.Status.ChargePercent = Measured<double>.From(power.BatteryLifePercent, Source);
        }

        if (power.BatteryFlag != BATTERY_FLAG_UNKNOWN)
        {
            bool charging = (power.BatteryFlag & BATTERY_FLAG_CHARGING) != 0;
            battery.Status.ChargeState = charging
                ? ChargeState.Charging
                : result.AcPower == AcPowerState.Connected
                    ? ChargeState.NotCharging
                    : ChargeState.Discharging;
        }

        // -1 means Windows has no estimate; it also returns -1 while on AC.
        if (power.BatteryLifeTime > 0 && power.BatteryLifeTime <= 7 * 24 * 3600)
        {
            battery.Status.EstimatedRuntimeSeconds = Measured<int>.From(power.BatteryLifeTime, Source);
        }
        else if (battery.Status.ChargeState == ChargeState.Discharging)
        {
            // Windows genuinely needs a few minutes of discharge before it will answer,
            // which is the "Calculating..." state described in SRS 14.
            battery.Status.RuntimeIsCalculating = true;
        }

        result.Batteries.Add(battery);
        return result;
    }
}
