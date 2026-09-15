using System.Text.Json;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Reporting;
using BatteryHealthChecker.Services;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>Report generation (SRS 16), including that missing values stay missing.</summary>
public class ReportWriterTests
{
    private static ReportContext Context(
        BatterySnapshot? snapshot = null, TemperatureUnit unit = TemperatureUnit.Celsius) => new(
        GeneratedAt: new DateTime(2026, 9, 15, 14, 5, 0),
        System: new SystemInfo
        {
            ComputerName = "TEST-PC",
            WindowsVersion = "Windows 11 Pro 24H2 (build 26100)",
            Manufacturer = "Contoso",
            Model = "Laptop 9000",
            ProcessorArchitecture = "X64",
            IsElevated = false,
        },
        Snapshot: snapshot ?? FullSnapshot(),
        TemperatureUnit: unit,
        ProductVersion: "1.0.0");

    private static BatterySnapshot FullSnapshot()
    {
        var info = new BatteryInfo
        {
            DeviceKey = @"\\?\acpi#pnp0c0a#1",
            Index = 0,
            Name = "Primary Pack",
            Manufacturer = "Contoso Cells",
            Model = "CX-9000",
            SerialNumber = "SN12345",
            Chemistry = BatteryChemistry.LithiumIon,
            CapacityUnit = CapacityUnit.MilliwattHours,
            DesignCapacity = Measured<long>.From(60_000, DataSource.BatteryIoctl),
            FullChargeCapacity = Measured<long>.From(52_200, DataSource.BatteryIoctl),
            CycleCount = Measured<int>.From(284, DataSource.WmiAcpi),
        };
        info.ContributingSources.Add(DataSource.BatteryIoctl);

        var status = new BatteryStatus
        {
            DeviceKey = info.DeviceKey,
            ChargeState = ChargeState.Charging,
            AcPower = AcPowerState.Connected,
            RemainingCapacity = Measured<long>.From(45_300, DataSource.BatteryIoctl),
            ChargePercent = Measured<double>.From(86.8, DataSource.Calculated),
            VoltageMillivolts = Measured<int>.From(11_400, DataSource.BatteryIoctl),
            TemperatureKelvin = Measured<double>.From(307.15, DataSource.BatteryIoctl),
        };
        status.ContributingSources.Add(DataSource.BatteryIoctl);

        return new BatterySnapshot
        {
            TimestampUtc = new DateTime(2026, 9, 15, 13, 5, 0, DateTimeKind.Utc),
            AcPower = AcPowerState.Connected,
            Batteries = new[]
            {
                new BatteryReading
                {
                    Info = info,
                    Status = status,
                    Health = BatteryHealthCalculator.Calculate(info.DesignCapacity, info.FullChargeCapacity),
                },
            },
        };
    }

    [Theory]
    [InlineData(ReportFormat.Html, "html")]
    [InlineData(ReportFormat.Txt, "txt")]
    [InlineData(ReportFormat.Csv, "csv")]
    [InlineData(ReportFormat.Json, "json")]
    public void Every_required_format_renders_non_empty_output(ReportFormat format, string extension)
    {
        var service = new ReportService(new LogService());

        string rendered = service.Render(format, Context());

        Assert.False(string.IsNullOrWhiteSpace(rendered));
        Assert.Equal(extension, service.GetWriter(format).Extension);
    }

    [Fact]
    public void Exported_file_names_carry_a_timestamp_in_the_specified_shape()
    {
        var service = new ReportService(new LogService());

        string name = service.BuildFileName(ReportFormat.Html, new DateTime(2026, 9, 15, 14, 5, 0));

        Assert.Equal("BatteryHealth_2026-09-15_1405.html", name);
    }

    [Fact]
    public void Text_report_contains_the_specified_sections_and_figures()
    {
        string report = new TextReportWriter().Render(Context());

        Assert.Contains("BATTERY HEALTH CHECKER REPORT", report);
        Assert.Contains("SYSTEM", report);
        Assert.Contains("CAPACITY", report);
        Assert.Contains("STATUS", report);
        Assert.Contains("ADDITIONAL", report);
        Assert.Contains("60,000 mWh", report);
        Assert.Contains("52,200 mWh", report);
        Assert.Contains("87%", report);
        Assert.Contains("284", report);
        Assert.Contains("Telemetry: Disabled", report);
        Assert.Contains("Cloud Upload: None", report);
    }

    [Fact]
    public void Reports_describe_health_as_an_estimate_not_a_diagnosis()
    {
        // SRS 6 forbids presenting the percentage as an absolute scientific diagnosis.
        string report = new TextReportWriter().Render(Context());

        Assert.Contains("estimate", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not an absolute diagnosis", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_emits_null_rather_than_zero_for_an_unavailable_reading()
    {
        string json = new JsonReportWriter().Render(Context(SnapshotWithNothingReported()));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement battery = document.RootElement.GetProperty("batteries")[0];

        JsonElement design = battery.GetProperty("capacity").GetProperty("design");
        Assert.False(design.GetProperty("available").GetBoolean());
        Assert.False(design.TryGetProperty("value", out _));

        JsonElement health = battery.GetProperty("health");
        Assert.False(health.GetProperty("available").GetBoolean());
        Assert.Equal("NoDesignCapacity", health.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public void Json_reports_the_health_figures_and_the_formula_used()
    {
        string json = new JsonReportWriter().Render(Context());

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement health = document.RootElement.GetProperty("batteries")[0].GetProperty("health");

        Assert.True(health.GetProperty("available").GetBoolean());
        Assert.Equal(87.0, health.GetProperty("healthPercent").GetDouble(), 6);
        Assert.Equal(13.0, health.GetProperty("wearPercent").GetDouble(), 6);
        Assert.Equal("Good", health.GetProperty("grade").GetString());
        Assert.Contains("DesignCapacity", health.GetProperty("formula").GetString());
    }

    [Fact]
    public void Csv_neutralises_a_firmware_string_that_looks_like_a_spreadsheet_formula()
    {
        BatterySnapshot snapshot = FullSnapshot();
        snapshot.Batteries[0].Info.Model = "=cmd|'/c calc'!A1";

        string csv = new CsvReportWriter().Render(Context(snapshot));

        Assert.DoesNotContain("\n=cmd", csv);
        Assert.Contains("'=cmd", csv);
    }

    [Fact]
    public void Html_escapes_markup_that_arrives_in_a_firmware_string()
    {
        BatterySnapshot snapshot = FullSnapshot();
        snapshot.Batteries[0].Info.Manufacturer = "<script>alert(1)</script>";

        string html = new HtmlReportWriter().Render(Context(snapshot));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Html_is_self_contained_with_no_external_resources()
    {
        // SRS 17: the report must open correctly with no network connection.
        string html = new HtmlReportWriter().Render(Context());

        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void A_machine_with_no_battery_produces_the_specified_message_in_every_format()
    {
        var service = new ReportService(new LogService());
        ReportContext context = Context(BatterySnapshot.Empty(AcPowerState.Connected));

        Assert.Contains("No battery detected", service.Render(ReportFormat.Txt, context));
        Assert.Contains("No battery detected", service.Render(ReportFormat.Html, context));
        Assert.Contains("No battery detected", service.Render(ReportFormat.Csv, context));

        using JsonDocument document = JsonDocument.Parse(service.Render(ReportFormat.Json, context));
        Assert.Equal(0, document.RootElement.GetProperty("batteryCount").GetInt32());
    }

    [Fact]
    public void Fahrenheit_selection_reaches_the_report()
    {
        string report = new TextReportWriter().Render(Context(unit: TemperatureUnit.Fahrenheit));

        Assert.Contains("93.2\u00b0F", report);
        Assert.DoesNotContain("\u00b0C", report);
    }

    private static BatterySnapshot SnapshotWithNothingReported()
    {
        var info = new BatteryInfo { DeviceKey = "unknown", Index = 0 };
        var status = new BatteryStatus { DeviceKey = "unknown" };

        return new BatterySnapshot
        {
            TimestampUtc = DateTime.UtcNow,
            Batteries = new[]
            {
                new BatteryReading
                {
                    Info = info,
                    Status = status,
                    Health = BatteryHealthCalculator.Calculate(
                        info.DesignCapacity, info.FullChargeCapacity),
                },
            },
        };
    }
}
