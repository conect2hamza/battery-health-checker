namespace BatteryHealthChecker.Models;

/// <summary>Why health could not be computed. Drives the explanatory text in the UI.</summary>
public enum HealthUnavailableReason
{
    None = 0,
    NoDesignCapacity,
    NoFullChargeCapacity,
    NonPositiveDesignCapacity,
    NotFinite,
}

/// <summary>
/// Outcome of the health calculation (SRS 6, 7, 20).
///
/// Both the raw and the display value are kept: SRS 6 allows capping the displayed
/// percentage to 0-100 but requires the original raw value to survive for
/// diagnostics and reporting.
/// </summary>
public sealed class HealthResult
{
    public static readonly HealthResult Unknown =
        new() { IsAvailable = false, Reason = HealthUnavailableReason.NoDesignCapacity };

    public bool IsAvailable { get; init; }
    public HealthUnavailableReason Reason { get; init; }

    /// <summary>Uncapped (FullCharge / Design) x 100 - preserved for diagnostics (SRS 6).</summary>
    public double RawHealthPercent { get; init; }

    /// <summary>Health clamped to 0-100 for presentation (SRS 6).</summary>
    public double HealthPercent { get; init; }

    /// <summary>100 - health, clamped to 0-100 (SRS 6).</summary>
    public double WearPercent { get; init; }

    public HealthGrade Grade { get; init; } = HealthGrade.Unknown;

    /// <summary>
    /// True when full-charge capacity exceeds design capacity, which is physically
    /// implausible for a worn cell and usually means the firmware is misreporting.
    /// SRS 20 requires flagging rather than silently presenting it as trustworthy.
    /// </summary>
    public bool IsSuspicious { get; init; }

    /// <summary>Human-readable explanation of the suspicion or of why health is missing.</summary>
    public string? Warning { get; init; }
}
