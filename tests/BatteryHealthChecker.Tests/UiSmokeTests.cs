using System.Runtime.ExceptionServices;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Reporting;
using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI;
using BatteryHealthChecker.UI.Controls;
using BatteryHealthChecker.UI.Views;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>
/// Constructs and paints every custom control and view.
///
/// These exist because a shipped build died at startup with "Control does not support
/// transparent background colors": four controls assigned Color.Transparent without
/// setting ControlStyles.SupportsTransparentBackColor, which a bare Control rejects at
/// construction. No test touched the UI, so nothing caught it. Constructing a control
/// is enough to catch that class of defect, and painting it catches the next one.
/// </summary>
public class UiSmokeTests
{
    /// <summary>
    /// Runs the body on an STA thread, as Windows Forms requires, and rethrows whatever
    /// it threw with the original stack intact.
    /// </summary>
    private static void OnUiThread(Action body)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromMinutes(2)), "The UI thread did not finish in time.");
        failure?.Throw();
    }

    /// <summary>Forces a real paint so OnPaint runs, not just the constructor.</summary>
    private static void Paint(Control control, int width = 640, int height = 260)
    {
        control.Width = width;
        control.Height = height;
        using var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
    }

    public static TheoryData<string> Themes => new() { "light", "dark" };

    private static Theme Resolve(string name) => name == "dark" ? Theme.Dark : Theme.Light;

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_custom_control_constructs_and_paints(string themeName)
    {
        Theme theme = Resolve(themeName);

        OnUiThread(() =>
        {
            // Assigning a transparent BackColor is what threw in the shipped build, and
            // it happens inside each of these constructors.
            var card = new CardPanel { Theme = theme, Heading = "Capacity", Subheading = "From firmware" };
            Paint(card);

            var row = new MetricRow { Theme = theme, Label = "Design Capacity", Value = "60,000 mWh" };
            Paint(row, 420, 30);

            var missing = new MetricRow { Theme = theme, Label = "Cycle Count", Value = Strings.NotAvailable };
            Paint(missing, 420, 30);

            var bar = new CapacityBar { Theme = theme, Label = "Full Charge", ValueText = "52,200 mWh" };
            bar.SetPercent(87, hasValue: true);
            Paint(bar, 420, 46);

            var emptyBar = new CapacityBar { Theme = theme, Label = "Current", ValueText = Strings.NotAvailable };
            emptyBar.SetPercent(0, hasValue: false);
            Paint(emptyBar, 420, 46);

            var headline = new HealthHeadline { Theme = theme };
            headline.SetHealth(
                BatteryHealthCalculator.Calculate(
                    Measured<long>.From(60_000, DataSource.BatteryIoctl),
                    Measured<long>.From(52_200, DataSource.BatteryIoctl)),
                "Battery Health");
            Paint(headline, 560, 210);

            var unknownHealth = new HealthHeadline { Theme = theme };
            unknownHealth.SetHealth(HealthResult.Unknown, "Battery Health");
            Paint(unknownHealth, 560, 210);

            var banner = new MessageBanner { Theme = theme, Message = "Something worth saying." };
            Paint(banner, 560, 60);

            var button = new FlatButton { Theme = theme, Text = "Refresh", IsPrimary = true };
            Paint(button, 120, 32);

            var nav = new NavigationRail { Theme = theme };
            nav.SetItems(new[] { "Dashboard", "Battery Details", "Reports", "Settings", "About" });
            Paint(nav, 178, 320);

            var stack = new StackLayout();
            stack.Add(new MetricRow { Theme = theme, Label = "A", Value = "1" });
            Paint(stack, 420, 60);

            var metricCard = new MetricCard("Right now");
            metricCard.ApplyTheme(theme);
            metricCard.AddRow("Battery Level").Value = "87%";
            metricCard.AddRow("Status").Value = "Charging";
            Paint(metricCard, 420, 160);
        });
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Every_view_constructs_and_renders_every_state(string themeName)
    {
        Theme theme = Resolve(themeName);

        OnUiThread(() =>
        {
            var log = new LogService();
            var reportService = new ReportService(log);
            var batteryService = new BatteryService(log);

            var views = new ViewBase[]
            {
                new DashboardView(),
                new DetailsView(),
                new ReportsView(reportService),
                new SettingsView(log),
                new AboutView(batteryService),
            };

            foreach (ViewBase view in views)
            {
                foreach (AppState state in States(theme))
                {
                    view.Render(state);
                }

                Paint(view, 900, 640);
                view.Dispose();
            }
        });
    }

    [Fact]
    public void The_main_window_constructs_and_applies_settings()
    {
        OnUiThread(() =>
        {
            var log = new LogService();
            var settings = new SettingsService(log);
            settings.Load();

            // Construction wires every view and control together; it does not scan
            // hardware, which only happens once the window loads.
            using var form = new MainForm(
                new BatteryService(log), new SystemInfoService(), settings, new ReportService(log), log);

            form.ApplyInitialSettings();
        });
    }

    /// <summary>The three shapes a view has to survive: a normal battery, a machine with
    /// none, and one that reports almost nothing.</summary>
    private static IEnumerable<AppState> States(Theme theme)
    {
        var settings = new AppSettings();
        var system = new SystemInfo
        {
            ComputerName = "TEST-PC",
            WindowsVersion = "Windows 11 Pro (build 26100)",
            Manufacturer = "Contoso",
            Model = "Laptop 9000",
            ProcessorArchitecture = "X64",
            IsElevated = false,
        };

        AppState Build(BatterySnapshot snapshot, int index = 0) => new()
        {
            Snapshot = snapshot,
            System = system,
            Settings = settings,
            Theme = theme,
            SelectedBatteryIndex = index,
        };

        yield return Build(Healthy());
        yield return Build(Healthy(), index: 1);
        yield return Build(BatterySnapshot.Empty(AcPowerState.Connected));
        yield return Build(NothingReported());
        yield return Build(Suspicious());
    }

    private static BatteryReading Reading(
        int index, Measured<long> design, Measured<long> full, Measured<long> remaining)
    {
        var info = new BatteryInfo
        {
            DeviceKey = $"device-{index}",
            Index = index,
            Name = $"Pack {index + 1}",
            Manufacturer = "Contoso Cells",
            Model = "CX-9000",
            SerialNumber = "SN-0001",
            Chemistry = BatteryChemistry.LithiumIon,
            CapacityUnit = CapacityUnit.MilliwattHours,
            DesignCapacity = design,
            FullChargeCapacity = full,
            CycleCount = Measured<int>.From(284, DataSource.WmiAcpi),
        };
        info.ContributingSources.Add(DataSource.BatteryIoctl);

        var status = new BatteryStatus
        {
            DeviceKey = info.DeviceKey,
            ChargeState = ChargeState.Discharging,
            AcPower = AcPowerState.Disconnected,
            RemainingCapacity = remaining,
            ChargePercent = Measured<double>.From(87, DataSource.Calculated),
            VoltageMillivolts = Measured<int>.From(11_732, DataSource.BatteryIoctl),
            RateMilliwatts = Measured<int>.From(-12_400, DataSource.BatteryIoctl),
            TemperatureKelvin = Measured<double>.From(307.15, DataSource.BatteryIoctl),
            EstimatedRuntimeSeconds = Measured<int>.From(11_880, DataSource.BatteryIoctl),
        };
        status.ContributingSources.Add(DataSource.BatteryIoctl);

        return new BatteryReading
        {
            Info = info,
            Status = status,
            Health = BatteryHealthCalculator.Calculate(design, full),
        };
    }

    private static BatterySnapshot Snapshot(params BatteryReading[] readings) => new()
    {
        TimestampUtc = DateTime.UtcNow,
        AcPower = AcPowerState.Disconnected,
        Batteries = readings,
    };

    private static BatterySnapshot Healthy() => Snapshot(
        Reading(0,
            Measured<long>.From(60_000, DataSource.BatteryIoctl),
            Measured<long>.From(52_200, DataSource.BatteryIoctl),
            Measured<long>.From(45_300, DataSource.BatteryIoctl)),
        Reading(1,
            Measured<long>.From(40_000, DataSource.BatteryIoctl),
            Measured<long>.From(23_600, DataSource.BatteryIoctl),
            Measured<long>.From(18_100, DataSource.BatteryIoctl)));

    /// <summary>A battery that answers nothing: every field must render as Not Available.</summary>
    private static BatterySnapshot NothingReported()
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
                    Health = BatteryHealthCalculator.Calculate(info.DesignCapacity, info.FullChargeCapacity),
                },
            },
            Issues = new[]
            {
                new CollectionIssue(DataSource.BatteryIoctl, "Access to this battery was denied.", true),
            },
        };
    }

    /// <summary>Full charge above design, which drives the warning banner path.</summary>
    private static BatterySnapshot Suspicious() => Snapshot(
        Reading(0,
            Measured<long>.From(50_000, DataSource.BatteryIoctl),
            Measured<long>.From(60_000, DataSource.BatteryIoctl),
            Measured<long>.From(45_000, DataSource.BatteryIoctl)));
}
