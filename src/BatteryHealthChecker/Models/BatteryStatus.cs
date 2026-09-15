namespace BatteryHealthChecker.Models;

/// <summary>
/// Fast-changing electrical state of one battery (SRS 5: Electrical, Status, Power,
/// Additional). Refreshed on the auto-refresh tick; <see cref="BatteryInfo"/> is not.
/// </summary>
public sealed class BatteryStatus
{
    public string DeviceKey { get; set; } = string.Empty;

    public ChargeState ChargeState { get; set; } = ChargeState.Unknown;
    public AcPowerState AcPower { get; set; } = AcPowerState.Unknown;
    public bool IsCritical { get; set; }

    /// <summary>Charge remaining, in the same unit as the capacities on <see cref="BatteryInfo"/>.</summary>
    public Measured<long> RemainingCapacity { get; set; } = Measured<long>.Unavailable();

    /// <summary>Charge level 0-100. Preferred from capacity ratio, falls back to the OS estimate.</summary>
    public Measured<double> ChargePercent { get; set; } = Measured<double>.Unavailable();

    public Measured<int> VoltageMillivolts { get; set; } = Measured<int>.Unavailable();

    /// <summary>Signed power flow in milliwatts: positive while charging, negative while discharging.</summary>
    public Measured<int> RateMilliwatts { get; set; } = Measured<int>.Unavailable();

    /// <summary>Signed current in milliamps. Derived from rate and voltage when both are present.</summary>
    public Measured<double> CurrentMilliamps { get; set; } = Measured<double>.Unavailable();

    /// <summary>Temperature in tenths of a Kelvin as reported; converted for display only.</summary>
    public Measured<double> TemperatureKelvin { get; set; } = Measured<double>.Unavailable();

    /// <summary>Estimated seconds of runtime left. Labelled as an estimate everywhere (SRS 14).</summary>
    public Measured<int> EstimatedRuntimeSeconds { get; set; } = Measured<int>.Unavailable();

    /// <summary>True when the system is discharging but no runtime estimate is ready yet (SRS 14).</summary>
    public bool RuntimeIsCalculating { get; set; }

    public List<DataSource> ContributingSources { get; } = new();
}
