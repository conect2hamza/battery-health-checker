using System.Text;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;

namespace BatteryHealthChecker.Reporting;

/// <summary>
/// CSV report (SRS 16). Emitted as a long table - one row per field - so that a
/// machine with two batteries and a machine with one produce the same column shape.
/// </summary>
public sealed class CsvReportWriter : IReportWriter
{
    public ReportFormat Format => ReportFormat.Csv;

    public string Extension => "csv";

    public string FileDialogFilter => "CSV report (*.csv)|*.csv";

    public string Render(ReportContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Battery,Section,Field,Value");

        void Row(string battery, string section, string field, string value) =>
            sb.AppendLine(string.Join(',', Escape(battery), Escape(section), Escape(field), Escape(value)));

        Row("", "Report", "Generated",
            context.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        Row("", "Report", "Product Version", context.ProductVersion);
        Row("", "System", "Computer", BatteryStatusService.Text(context.System.ComputerName));
        Row("", "System", "Windows Version", BatteryStatusService.Text(context.System.WindowsVersion));
        Row("", "System", "Manufacturer", BatteryStatusService.Text(context.System.Manufacturer));
        Row("", "System", "Model", BatteryStatusService.Text(context.System.Model));
        Row("", "System", "Architecture", BatteryStatusService.Text(context.System.ProcessorArchitecture));
        Row("", "System", "AC Power", BatteryStatusService.AcPower(context.Snapshot.AcPower));

        if (!context.Snapshot.HasBattery)
        {
            Row("", "Battery", "Detected", "No battery detected");
            return sb.ToString();
        }

        foreach (BatteryReading reading in context.Snapshot.Batteries)
        {
            BatteryInfo info = reading.Info;
            BatteryStatus status = reading.Status;
            string name = $"Battery {info.Index + 1}";

            Row(name, "Identification", "Name", BatteryStatusService.Text(info.Name));
            Row(name, "Identification", "Manufacturer", BatteryStatusService.Text(info.Manufacturer));
            Row(name, "Identification", "Model", BatteryStatusService.Text(info.Model));
            Row(name, "Identification", "Serial Number", BatteryStatusService.Text(info.SerialNumber));
            Row(name, "Identification", "Chemistry", BatteryStatusService.Chemistry(info));
            Row(name, "Identification", "Manufacture Date", BatteryStatusService.ManufactureDate(info.ManufactureDate));

            Row(name, "Capacity", "Design Capacity", BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit));
            Row(name, "Capacity", "Full Charge Capacity", BatteryStatusService.Capacity(info.FullChargeCapacity, info.CapacityUnit));
            Row(name, "Capacity", "Current Capacity", BatteryStatusService.Capacity(status.RemainingCapacity, info.CapacityUnit));
            Row(name, "Capacity", "Health", BatteryStatusService.Health(reading.Health));
            Row(name, "Capacity", "Wear", BatteryStatusService.Wear(reading.Health));
            Row(name, "Capacity", "Condition", BatteryStatusService.Grade(reading.Health));
            Row(name, "Capacity", "Health (uncapped)", reading.Health.IsAvailable
                ? BatteryStatusService.Percent(reading.Health.RawHealthPercent, 2)
                : Strings.NotAvailable);

            Row(name, "Status", "Charge", BatteryStatusService.Percent(status.ChargePercent));
            Row(name, "Status", "Status", BatteryStatusService.ChargeState(status.ChargeState));
            Row(name, "Status", "AC Power", BatteryStatusService.AcPower(status.AcPower));

            Row(name, "Additional", "Cycle Count", BatteryStatusService.CycleCount(info.CycleCount));
            Row(name, "Additional", "Temperature", BatteryStatusService.Temperature(status.TemperatureKelvin, context.TemperatureUnit));
            Row(name, "Additional", "Voltage", BatteryStatusService.Voltage(status.VoltageMillivolts));
            Row(name, "Additional", "Design Voltage", BatteryStatusService.Voltage(info.DesignVoltageMillivolts));
            Row(name, "Additional", "Power Flow", BatteryStatusService.Power(status.RateMilliwatts));
            Row(name, "Additional", "Current", BatteryStatusService.Current(status.CurrentMilliamps));
            Row(name, "Additional", "Estimated Runtime",
                BatteryStatusService.Runtime(status.EstimatedRuntimeSeconds, status.RuntimeIsCalculating));

            Row(name, "Diagnostics", "Identity sources", BatteryStatusService.SourceList(info.ContributingSources));
            Row(name, "Diagnostics", "Status sources", BatteryStatusService.SourceList(status.ContributingSources));
            if (reading.Health.Warning is { } warning) Row(name, "Diagnostics", "Note", warning);
        }

        foreach (CollectionIssue issue in context.Snapshot.Issues)
        {
            Row("", "Notes", BatteryStatusService.SourceName(issue.Source), issue.Message);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quotes a field for CSV. A leading =, +, - or @ is prefixed with a single quote:
    /// battery model strings come from firmware, and a spreadsheet would otherwise
    /// interpret such a value as a formula.
    /// </summary>
    private static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@') value = "'" + value;

        bool needsQuotes = value.Contains(',') || value.Contains('"')
            || value.Contains('\n') || value.Contains('\r');

        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }
}
