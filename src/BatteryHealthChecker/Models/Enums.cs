namespace BatteryHealthChecker.Models;

/// <summary>Charge/discharge state of a battery (SRS 5: Status).</summary>
public enum ChargeState
{
    Unknown = 0,
    Charging,
    Discharging,
    FullyCharged,
    NotCharging,
}

/// <summary>Mains power state (SRS 5: Power).</summary>
public enum AcPowerState
{
    Unknown = 0,
    Connected,
    Disconnected,
}

/// <summary>Health bands from SRS 7.</summary>
public enum HealthGrade
{
    Unknown = 0,
    Critical,
    Poor,
    Fair,
    Good,
    Excellent,
}

/// <summary>Cell chemistry as reported by the hardware. Never guessed.</summary>
public enum BatteryChemistry
{
    Unknown = 0,
    LithiumIon,
    LithiumPolymer,
    NickelMetalHydride,
    NickelCadmium,
    NickelZinc,
    LeadAcid,
    ZincAir,
    RechargeableAlkalineManganese,
    Other,
}

/// <summary>
/// The unit capacities are expressed in. Windows battery IOCTLs report mWh unless the
/// device sets BATTERY_CAPACITY_RELATIVE, in which case the numbers are unitless and
/// must not be labelled mWh (SRS 2: never fabricate; SRS 20: validate before display).
/// </summary>
public enum CapacityUnit
{
    Unknown = 0,
    MilliwattHours,
    Relative,
}

public enum TemperatureUnit
{
    Celsius = 0,
    Fahrenheit = 1,
}

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public enum ReportFormat
{
    Html = 0,
    Txt = 1,
    Csv = 2,
    Json = 3,
}
