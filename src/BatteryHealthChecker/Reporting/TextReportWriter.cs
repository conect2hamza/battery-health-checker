using System.Text;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;

namespace BatteryHealthChecker.Reporting;

/// <summary>Plain-text report, laid out as in the SRS 16 example.</summary>
public sealed class TextReportWriter : IReportWriter
{
    public ReportFormat Format => ReportFormat.Txt;

    public string Extension => "txt";

    public string FileDialogFilter => "Text report (*.txt)|*.txt";

    public string Render(ReportContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BATTERY HEALTH CHECKER REPORT");
        sb.AppendLine();
        sb.AppendLine("Generated:");
        sb.AppendLine(context.GeneratedAt.ToString("dd MMMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        sb.AppendLine();

        Section(sb, "SYSTEM");
        Field(sb, "Computer", BatteryStatusService.Text(context.System.ComputerName));
        Field(sb, "Windows Version", BatteryStatusService.Text(context.System.WindowsVersion));
        Field(sb, "Manufacturer", BatteryStatusService.Text(context.System.Manufacturer));
        Field(sb, "Model", BatteryStatusService.Text(context.System.Model));
        Field(sb, "Architecture", BatteryStatusService.Text(context.System.ProcessorArchitecture));
        sb.AppendLine();

        if (!context.Snapshot.HasBattery)
        {
            sb.AppendLine("No battery detected.");
            sb.AppendLine();
            sb.AppendLine("This device may be a desktop computer or Windows");
            sb.AppendLine("may be unable to provide battery information.");
            AppendFooter(sb, context);
            return sb.ToString();
        }

        foreach (BatteryReading reading in context.Snapshot.Batteries)
        {
            BatteryInfo info = reading.Info;
            BatteryStatus status = reading.Status;

            sb.AppendLine($"=== {info.DisplayName} (Battery {info.Index + 1} of {context.Snapshot.Batteries.Count}) ===");
            sb.AppendLine();

            Section(sb, "BATTERY");
            Field(sb, "Manufacturer", BatteryStatusService.Text(info.Manufacturer));
            Field(sb, "Model", BatteryStatusService.Text(info.Model));
            Field(sb, "Serial Number", BatteryStatusService.Text(info.SerialNumber));
            Field(sb, "Chemistry", BatteryStatusService.Chemistry(info));
            Field(sb, "Manufacture Date", BatteryStatusService.ManufactureDate(info.ManufactureDate));
            sb.AppendLine();

            Section(sb, "CAPACITY");
            Field(sb, "Design Capacity", BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit));
            Field(sb, "Full Charge Capacity", BatteryStatusService.Capacity(info.FullChargeCapacity, info.CapacityUnit));
            Field(sb, "Current Capacity", BatteryStatusService.Capacity(status.RemainingCapacity, info.CapacityUnit));
            Field(sb, "Health", BatteryStatusService.Health(reading.Health));
            Field(sb, "Wear", BatteryStatusService.Wear(reading.Health));
            Field(sb, "Condition", BatteryStatusService.Grade(reading.Health));
            if (reading.Health.IsAvailable)
            {
                Field(sb, "Health (uncapped)",
                    BatteryStatusService.Percent(reading.Health.RawHealthPercent, 2));
            }
            if (reading.Health.Warning is { } warning) Field(sb, "Note", warning);
            sb.AppendLine();

            Section(sb, "STATUS");
            Field(sb, "Charge", BatteryStatusService.Percent(status.ChargePercent));
            Field(sb, "Status", BatteryStatusService.ChargeState(status.ChargeState));
            Field(sb, "AC Power", BatteryStatusService.AcPower(status.AcPower));
            sb.AppendLine();

            Section(sb, "ADDITIONAL");
            Field(sb, "Cycle Count", BatteryStatusService.CycleCount(info.CycleCount));
            Field(sb, "Temperature", BatteryStatusService.Temperature(status.TemperatureKelvin, context.TemperatureUnit));
            Field(sb, "Voltage", BatteryStatusService.Voltage(status.VoltageMillivolts));
            Field(sb, "Design Voltage", BatteryStatusService.Voltage(info.DesignVoltageMillivolts));
            Field(sb, "Power Flow", BatteryStatusService.Power(status.RateMilliwatts));
            Field(sb, "Current", BatteryStatusService.Current(status.CurrentMilliamps));
            Field(sb, "Estimated Runtime",
                BatteryStatusService.Runtime(status.EstimatedRuntimeSeconds, status.RuntimeIsCalculating));
            sb.AppendLine();

            Section(sb, "DATA SOURCES");
            Field(sb, "Identity and capacity", BatteryStatusService.SourceList(info.ContributingSources));
            Field(sb, "Live status", BatteryStatusService.SourceList(status.ContributingSources));
            sb.AppendLine();
        }

        if (context.Snapshot.Issues.Count > 0)
        {
            Section(sb, "NOTES");
            foreach (CollectionIssue issue in context.Snapshot.Issues) sb.AppendLine($"- {issue.Message}");
            sb.AppendLine();
        }

        AppendFooter(sb, context);
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine(title);
        sb.AppendLine("-------------------------");
    }

    private static void Field(StringBuilder sb, string label, string value) =>
        sb.AppendLine($"{label}: {value}");

    private static void AppendFooter(StringBuilder sb, ReportContext context)
    {
        sb.AppendLine("-------------------------");
        sb.AppendLine($"Battery Health Checker {context.ProductVersion}");
        sb.AppendLine(ReportDisclaimer.Text);
        sb.AppendLine("Telemetry: Disabled");
        sb.AppendLine("Cloud Upload: None");
    }
}

/// <summary>
/// The wording required by SRS 6: health must be described as an estimate based on
/// reported capacity, never as an absolute scientific diagnosis.
/// </summary>
public static class ReportDisclaimer
{
    public const string Text =
        "Battery health is an estimate derived from the capacity values reported by the "
        + "battery firmware. It is not an absolute diagnosis of the battery's condition.";

    public const string RuntimeText =
        "Estimated runtime is provided by Windows and varies with workload. It is not a guarantee.";
}
