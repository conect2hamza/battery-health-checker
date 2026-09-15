namespace BatteryHealthChecker.Models;

/// <summary>
/// Static, slow-changing facts about one battery: identity, chemistry and the two
/// capacities that drive the health calculation (SRS 5).
/// Every field is a <see cref="Measured{T}"/> or a nullable string so that "the
/// hardware did not tell us" is representable without inventing a value.
/// </summary>
public sealed class BatteryInfo
{
    /// <summary>Stable per-device key used to correlate readings across collectors.</summary>
    public string DeviceKey { get; set; } = string.Empty;

    /// <summary>Index used for the UI selector when several batteries are present (SRS 11).</summary>
    public int Index { get; set; }

    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? UniqueId { get; set; }
    public BatteryChemistry Chemistry { get; set; } = BatteryChemistry.Unknown;
    public string? ChemistryRaw { get; set; }
    public DateTime? ManufactureDate { get; set; }

    public CapacityUnit CapacityUnit { get; set; } = CapacityUnit.Unknown;

    /// <summary>Capacity the battery had when new. Denominator of the health formula (SRS 6).</summary>
    public Measured<long> DesignCapacity { get; set; } = Measured<long>.Unavailable();

    /// <summary>Capacity the battery can hold today. Numerator of the health formula (SRS 6).</summary>
    public Measured<long> FullChargeCapacity { get; set; } = Measured<long>.Unavailable();

    /// <summary>Charge/discharge cycles reported by the hardware. Never estimated (SRS 13).</summary>
    public Measured<int> CycleCount { get; set; } = Measured<int>.Unavailable();

    /// <summary>Nominal design voltage in millivolts.</summary>
    public Measured<int> DesignVoltageMillivolts { get; set; } = Measured<int>.Unavailable();

    /// <summary>True when the device reports itself as a system (not UPS/peripheral) battery.</summary>
    public bool IsSystemBattery { get; set; } = true;

    /// <summary>Collectors that contributed to this record, for the diagnostics section of a report.</summary>
    public List<DataSource> ContributingSources { get; } = new();

    public string DisplayName => !string.IsNullOrWhiteSpace(Name)
        ? Name!
        : !string.IsNullOrWhiteSpace(Model)
            ? Model!
            : $"Battery {Index + 1}";
}
