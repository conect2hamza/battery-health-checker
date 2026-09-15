using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Services;

/// <summary>
/// Implements the health and wear calculation from SRS 6 together with the validation
/// rules from SRS 20.
///
/// The calculation is deliberately a pure function of two readings: it has no access
/// to hardware, so it is fully unit-testable and cannot mask a collection problem by
/// substituting a value of its own.
/// </summary>
public static class BatteryHealthCalculator
{
    /// <summary>
    /// Full-charge readings above this multiple of design capacity are not a worn cell
    /// misreporting slightly - they indicate the firmware is reporting a different
    /// quantity or unit entirely, so the result is flagged rather than presented.
    /// </summary>
    internal const double SuspiciousRatioThreshold = 1.0;

    public static HealthResult Calculate(Measured<long> designCapacity, Measured<long> fullChargeCapacity)
    {
        if (!designCapacity.IsAvailable)
        {
            return new HealthResult
            {
                IsAvailable = false,
                Reason = HealthUnavailableReason.NoDesignCapacity,
                Warning = "This battery does not report a design capacity, so health cannot be calculated.",
            };
        }

        if (!fullChargeCapacity.IsAvailable)
        {
            return new HealthResult
            {
                IsAvailable = false,
                Reason = HealthUnavailableReason.NoFullChargeCapacity,
                Warning = "This battery does not report a full-charge capacity, so health cannot be calculated.",
            };
        }

        long design = designCapacity.Value;
        long full = fullChargeCapacity.Value;

        // SRS 20: a non-positive denominator must never reach the division.
        if (design <= 0)
        {
            return new HealthResult
            {
                IsAvailable = false,
                Reason = HealthUnavailableReason.NonPositiveDesignCapacity,
                Warning = "The reported design capacity is not a positive value, so health cannot be calculated.",
            };
        }

        if (full < 0)
        {
            return new HealthResult
            {
                IsAvailable = false,
                // The value was reported; it is simply not usable. Saying "not reported"
                // here would misdescribe the hardware in the report (BUG-006).
                Reason = HealthUnavailableReason.InvalidFullChargeCapacity,
                Warning = "The reported full-charge capacity is negative, so health cannot be calculated.",
            };
        }

        double raw = (double)full / design * 100.0;

        // SRS 20: never surface NaN or infinity. With design > 0 and full >= 0 this is
        // unreachable in practice, which is exactly why it is worth asserting.
        if (!double.IsFinite(raw))
        {
            return new HealthResult
            {
                IsAvailable = false,
                Reason = HealthUnavailableReason.NotFinite,
                Warning = "The reported capacities produced an invalid health value.",
            };
        }

        // SRS 6: cap for presentation, preserve the raw value for diagnostics.
        double capped = Math.Clamp(raw, 0.0, 100.0);
        bool suspicious = full > design * SuspiciousRatioThreshold;

        return new HealthResult
        {
            IsAvailable = true,
            Reason = HealthUnavailableReason.None,
            RawHealthPercent = raw,
            HealthPercent = capped,
            WearPercent = Math.Clamp(100.0 - capped, 0.0, 100.0),
            Grade = Classify(capped),
            IsSuspicious = suspicious,
            Warning = suspicious
                ? "The reported full-charge capacity is higher than the design capacity. "
                  + "This is an unusual hardware reading and the health figure may not be meaningful."
                : null,
        };
    }

    /// <summary>Applies the SRS 7 thresholds.</summary>
    public static HealthGrade Classify(double healthPercent) => healthPercent switch
    {
        >= 90 => HealthGrade.Excellent,
        >= 80 => HealthGrade.Good,
        >= 60 => HealthGrade.Fair,
        >= 40 => HealthGrade.Poor,
        >= 0 => HealthGrade.Critical,
        _ => HealthGrade.Unknown,
    };

    public static string GradeLabel(HealthGrade grade) => grade switch
    {
        HealthGrade.Excellent => "EXCELLENT",
        HealthGrade.Good => "GOOD",
        HealthGrade.Fair => "FAIR",
        HealthGrade.Poor => "POOR",
        HealthGrade.Critical => "CRITICAL",
        _ => "UNKNOWN",
    };
}
