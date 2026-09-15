using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;

namespace BatteryHealthChecker.Reporting;

/// <summary>
/// JSON report (SRS 16).
///
/// Every reading is emitted as an object carrying the value, its unit and the source
/// that produced it. An unavailable reading serializes as <c>null</c> rather than 0,
/// so a consumer of the file cannot mistake a missing value for a real one (SRS 2).
/// </summary>
public sealed class JsonReportWriter : IReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The document is built from anonymous types with camelCase members; the policy
        // keeps the named HealthDocument record consistent with them.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public ReportFormat Format => ReportFormat.Json;

    public string Extension => "json";

    public string FileDialogFilter => "JSON report (*.json)|*.json";

    public string Render(ReportContext context)
    {
        var document = new
        {
            report = new
            {
                product = "Battery Health Checker",
                version = context.ProductVersion,
                generated = context.GeneratedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                generatedUtc = context.Snapshot.TimestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                temperatureUnit = context.TemperatureUnit.ToString(),
                disclaimer = ReportDisclaimer.Text,
                telemetry = "Disabled",
                cloudUpload = "None",
            },
            system = new
            {
                computer = context.System.ComputerName,
                windowsVersion = context.System.WindowsVersion,
                windowsBuild = context.System.WindowsBuild,
                manufacturer = context.System.Manufacturer,
                model = context.System.Model,
                architecture = context.System.ProcessorArchitecture,
                runningElevated = context.System.IsElevated,
            },
            power = new
            {
                acPower = context.Snapshot.AcPower.ToString(),
            },
            batteryCount = context.Snapshot.Batteries.Count,
            batteries = context.Snapshot.Batteries.Select(Describe).ToArray(),
            notes = context.Snapshot.Issues
                .Select(i => new { source = BatteryStatusService.SourceName(i.Source), message = i.Message })
                .ToArray(),
        };

        return JsonSerializer.Serialize(document, Options);
    }

    private static object Describe(BatteryReading reading)
    {
        BatteryInfo info = reading.Info;
        BatteryStatus status = reading.Status;
        string capacityUnit = info.CapacityUnit switch
        {
            CapacityUnit.MilliwattHours => "mWh",
            CapacityUnit.Relative => "relative",
            _ => "unknown",
        };

        return new
        {
            index = info.Index,
            displayName = info.DisplayName,
            identification = new
            {
                name = info.Name,
                manufacturer = info.Manufacturer,
                model = info.Model,
                serialNumber = info.SerialNumber,
                uniqueId = info.UniqueId,
                chemistry = info.Chemistry == BatteryChemistry.Unknown ? null : info.Chemistry.ToString(),
                chemistryRaw = info.ChemistryRaw,
                manufactureDate = info.ManufactureDate?.ToString("yyyy-MM-dd"),
                isSystemBattery = info.IsSystemBattery,
                isSystemBatteryReported = info.IsSystemBattery.HasValue,
            },
            capacity = new
            {
                unit = capacityUnit,
                design = Reading(info.DesignCapacity, capacityUnit),
                fullCharge = Reading(info.FullChargeCapacity, capacityUnit),
                remaining = Reading(status.RemainingCapacity, capacityUnit),
            },
            health = DescribeHealth(reading.Health),
            status = new
            {
                chargeState = status.ChargeState.ToString(),
                acPower = status.AcPower.ToString(),
                critical = status.IsCritical,
                chargePercent = Reading(status.ChargePercent, "%"),
            },
            electrical = new
            {
                voltage = Reading(status.VoltageMillivolts, "mV"),
                designVoltage = Reading(info.DesignVoltageMillivolts, "mV"),
                powerFlow = Reading(status.RateMilliwatts, "mW"),
                current = Reading(status.CurrentMilliamps, "mA"),
            },
            additional = new
            {
                cycleCount = Reading(info.CycleCount, "cycles"),
                temperatureCelsius = status.TemperatureKelvin.IsAvailable
                    ? Reading(Measured<double>.From(
                        Math.Round(status.TemperatureKelvin.Value - 273.15, 2),
                        status.TemperatureKelvin.Source), "C")
                    : Reading(Measured<double>.Unavailable(status.TemperatureKelvin.Note), "C"),
                estimatedRuntimeSeconds = Reading(status.EstimatedRuntimeSeconds, "s"),
                estimatedRuntimeCalculating = status.RuntimeIsCalculating,
                estimatedRuntimeNote = ReportDisclaimer.RuntimeText,
            },
            dataSources = new
            {
                identity = info.ContributingSources.Distinct().Select(s => s.ToString()).ToArray(),
                status = status.ContributingSources.Distinct().Select(s => s.ToString()).ToArray(),
            },
        };
    }

    /// <summary>
    /// Serializes the health result. An unavailable result still emits the numeric
    /// fields as 0 alongside <c>available: false</c> and a reason, so a consumer that
    /// ignores the flag cannot silently read a zero as a real measurement.
    /// </summary>
    private static HealthDocument DescribeHealth(HealthResult health) => new(
        Available: health.IsAvailable,
        HealthPercent: health.IsAvailable ? Math.Round(health.HealthPercent, 2) : 0,
        RawHealthPercent: health.IsAvailable ? Math.Round(health.RawHealthPercent, 4) : 0,
        WearPercent: health.IsAvailable ? Math.Round(health.WearPercent, 2) : 0,
        Grade: health.IsAvailable ? health.Grade.ToString() : null,
        Suspicious: health.IsSuspicious,
        Warning: health.Warning,
        Formula: "(FullChargeCapacity / DesignCapacity) * 100",
        UnavailableReason: health.IsAvailable ? null : health.Reason.ToString());

    private sealed record HealthDocument(
        bool Available,
        double HealthPercent,
        double RawHealthPercent,
        double WearPercent,
        string? Grade,
        bool Suspicious,
        string? Warning,
        string Formula,
        string? UnavailableReason);

    /// <summary>Serializes a reading, collapsing "unavailable" to a null value with a reason.</summary>
    private static object Reading<T>(Measured<T> measured, string unit) where T : struct => new
    {
        value = measured.IsAvailable ? (object?)measured.Value : null,
        unit,
        available = measured.IsAvailable,
        source = measured.IsAvailable ? measured.Source.ToString() : null,
        note = measured.Note,
    };
}
