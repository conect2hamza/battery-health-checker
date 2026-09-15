using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Reporting;

/// <summary>Everything a report writer needs. Writers do no data collection of their own.</summary>
/// <param name="GeneratedAt">Local time the report was produced.</param>
/// <param name="System">Host details for the SYSTEM section (SRS 16).</param>
/// <param name="Snapshot">The scan being reported.</param>
/// <param name="TemperatureUnit">The unit the user selected (SRS 12).</param>
/// <param name="ProductVersion">Version string for the report footer.</param>
public sealed record ReportContext(
    DateTime GeneratedAt,
    SystemInfo System,
    BatterySnapshot Snapshot,
    TemperatureUnit TemperatureUnit,
    string ProductVersion);

/// <summary>A report serializer for one of the formats required by SRS 16.</summary>
public interface IReportWriter
{
    ReportFormat Format { get; }

    /// <summary>File extension without the leading dot.</summary>
    string Extension { get; }

    /// <summary>Filter fragment for the save dialog.</summary>
    string FileDialogFilter { get; }

    string Render(ReportContext context);
}
