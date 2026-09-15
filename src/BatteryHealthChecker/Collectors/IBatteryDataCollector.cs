using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Collectors;

/// <summary>
/// One battery as seen by a single collector, before cross-source merging.
/// </summary>
internal sealed class CollectedBattery
{
    /// <summary>Position in the collector's own enumeration order.</summary>
    public int Ordinal { get; init; }

    /// <summary>
    /// Hardware-stable identifier (the ACPI unique ID). When two collectors agree on
    /// this string they are describing the same physical battery, which is what makes
    /// cross-source merging safe on multi-battery laptops (SRS 11).
    /// </summary>
    public string? UniqueId { get; set; }

    public BatteryInfo Info { get; init; } = new();
    public BatteryStatus Status { get; init; } = new();
}

/// <summary>Everything one collector managed to read in a single pass.</summary>
internal sealed class CollectionResult
{
    public List<CollectedBattery> Batteries { get; } = new();
    public AcPowerState AcPower { get; set; } = AcPowerState.Unknown;
    public List<CollectionIssue> Issues { get; } = new();

    /// <summary>True when the source positively reported that the machine has no battery.</summary>
    public bool ReportedNoBattery { get; set; }
}

/// <summary>
/// A source of battery data (SRS 4). Implementations must never throw: a source that
/// fails records a <see cref="CollectionIssue"/> and returns whatever it did obtain,
/// so that one unavailable mechanism can never take the application down (SRS 19).
/// </summary>
internal interface IBatteryDataCollector
{
    DataSource Source { get; }

    /// <summary>Human-readable name used in the diagnostics section of reports.</summary>
    string Name { get; }

    /// <summary>
    /// True when the collector only produces fast-changing status and can therefore be
    /// re-run on every auto-refresh tick without re-reading static data (SRS 24).
    /// </summary>
    bool SupportsStatusOnly { get; }

    CollectionResult Collect(bool statusOnly, CancellationToken cancellationToken);
}
