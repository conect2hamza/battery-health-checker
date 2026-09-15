namespace BatteryHealthChecker.Models;

/// <summary>One battery's static info, live status and derived health at a point in time.</summary>
public sealed class BatteryReading
{
    public required BatteryInfo Info { get; init; }
    public required BatteryStatus Status { get; init; }
    public required HealthResult Health { get; init; }

    public string DisplayName => Info.DisplayName;
}

/// <summary>
/// Immutable result of a full scan. The UI renders snapshots and never queries
/// hardware itself (SRS 4: keep data collection completely separate from the UI).
/// </summary>
public sealed class BatterySnapshot
{
    public required DateTime TimestampUtc { get; init; }
    public required IReadOnlyList<BatteryReading> Batteries { get; init; }

    /// <summary>System-wide AC state, valid even when no battery is present.</summary>
    public AcPowerState AcPower { get; init; } = AcPowerState.Unknown;

    /// <summary>True when Windows reports no battery at all (SRS 19: desktop case).</summary>
    public bool HasBattery => Batteries.Count > 0;

    /// <summary>
    /// Non-fatal problems hit while collecting, e.g. a device that refused a query.
    /// Shown as a banner rather than a crash (SRS 19).
    /// </summary>
    public IReadOnlyList<CollectionIssue> Issues { get; init; } = Array.Empty<CollectionIssue>();

    public static BatterySnapshot Empty(AcPowerState ac = AcPowerState.Unknown) => new()
    {
        TimestampUtc = DateTime.UtcNow,
        Batteries = Array.Empty<BatteryReading>(),
        AcPower = ac,
    };
}

/// <summary>A problem encountered during collection, and whether elevation might fix it.</summary>
/// <param name="Source">Which collector reported it.</param>
/// <param name="Message">Plain-language description for the user.</param>
/// <param name="ElevationMayHelp">
/// True only when the failure was an access-denied style error. SRS 19 forbids
/// recommending administrator mode unless elevation may genuinely resolve the issue.
/// </param>
public readonly record struct CollectionIssue(DataSource Source, string Message, bool ElevationMayHelp);
