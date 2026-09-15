using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI.Controls;

namespace BatteryHealthChecker.UI.Views;

/// <summary>
/// The main dashboard (SRS 8). Carries only the information a non-technical user needs
/// at a glance; everything else lives in Battery Details (SRS 25).
/// </summary>
public sealed class DashboardView : ViewBase
{
    private readonly StackLayout _stack = new();
    private readonly MessageBanner _banner = new() { Visible = false };
    private readonly CardPanel _headlineCard = new();
    private readonly HealthHeadline _headline = new();
    private readonly CardPanel _capacityCard = new();
    private readonly CapacityBar _designBar = new();
    private readonly CapacityBar _fullBar = new();
    private readonly CapacityBar _currentBar = new();
    private readonly MetricCard _keyFacts = new("Capacity");
    private readonly MetricCard _liveStatus = new("Right now");
    private readonly CardPanel _emptyCard = new();
    private readonly Label _emptyTitle = new();
    private readonly Label _emptyBody = new();

    private readonly MetricRow _designRow;
    private readonly MetricRow _fullRow;
    private readonly MetricRow _wearRow;
    private readonly MetricRow _cycleRow;
    private readonly MetricRow _levelRow;
    private readonly MetricRow _statusRow;
    private readonly MetricRow _acRow;
    private readonly MetricRow _runtimeRow;
    private readonly MetricRow _temperatureRow;

    public DashboardView()
    {
        AutoScroll = true;
        Padding = new Padding(22, 18, 22, 18);

        _headlineCard.Padding = new Padding(16, 10, 16, 14);
        _headlineCard.AutoSize = true;
        _headlineCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _headline.Dock = DockStyle.Top;
        _headlineCard.Controls.Add(_headline);

        _capacityCard.Heading = "Capacity over time";
        _capacityCard.AutoSize = true;
        _capacityCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _capacityCard.Padding = new Padding(20, 48, 20, 16);
        var capacityStack = new StackLayout();
        capacityStack.Add(_designBar, 10);
        capacityStack.Add(_fullBar, 10);
        capacityStack.Add(_currentBar, 0);
        _capacityCard.Controls.Add(capacityStack);

        _designRow = _keyFacts.AddRow("Design Capacity");
        _fullRow = _keyFacts.AddRow("Full Charge Capacity");
        _wearRow = _keyFacts.AddRow("Battery Wear");
        _cycleRow = _keyFacts.AddRow("Cycle Count");

        _levelRow = _liveStatus.AddRow("Battery Level");
        _statusRow = _liveStatus.AddRow("Status");
        _acRow = _liveStatus.AddRow("AC Power");
        _runtimeRow = _liveStatus.AddRow("Estimated Runtime");
        _temperatureRow = _liveStatus.AddRow("Temperature");

        BuildEmptyCard();

        _stack.Add(_banner);
        _stack.Add(_emptyCard);
        _stack.Add(_headlineCard);
        _stack.Add(_capacityCard);
        _stack.Add(_keyFacts);
        _stack.Add(_liveStatus, 4);
        Controls.Add(_stack);
    }

    public override string Title => "Dashboard";

    private void BuildEmptyCard()
    {
        _emptyCard.AutoSize = true;
        _emptyCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _emptyCard.Padding = new Padding(24, 26, 24, 26);
        _emptyCard.Visible = false;

        _emptyBody.AutoSize = false;
        _emptyBody.Dock = DockStyle.Top;
        _emptyBody.Height = 46;
        _emptyBody.Font = Fonts.Base;
        // SRS 19: exact wording for a machine with no battery.
        _emptyBody.Text = "This device may be a desktop computer or Windows "
                          + "may be unable to provide battery information.";

        _emptyTitle.AutoSize = false;
        _emptyTitle.Dock = DockStyle.Top;
        _emptyTitle.Height = 30;
        _emptyTitle.Font = Fonts.CardTitle;
        _emptyTitle.Text = "No battery detected.";

        _emptyCard.Controls.Add(_emptyBody);
        _emptyCard.Controls.Add(_emptyTitle);
    }

    public override void Render(AppState state)
    {
        Theme theme = state.Theme;
        ApplyTheme(theme);

        BatteryReading? reading = state.SelectedBattery;

        // A machine whose only batteries are peripherals (a UPS, a dock) has no battery
        // of its own to report on, and saying otherwise would be the BUG-001 failure.
        bool hasBattery = reading is not null && !state.Snapshot.SystemReportsNoBattery;

        _emptyCard.Visible = !hasBattery;
        _headlineCard.Visible = hasBattery;
        _capacityCard.Visible = hasBattery;
        _keyFacts.Visible = hasBattery;
        _liveStatus.Visible = hasBattery;

        RenderBanner(state, reading);

        if (reading is null)
        {
            // AC state is still meaningful on a desktop, so keep it accurate rather
            // than hiding it: the machine is plugged in even with no battery.
            _acRow.Value = BatteryStatusService.AcPower(state.Snapshot.AcPower);
            return;
        }

        BatteryInfo info = reading.Info;
        BatteryStatus status = reading.Status;
        HealthResult health = reading.Health;

        string caption = info.IsConfirmedPeripheral
            ? $"{info.DisplayName} - not this PC's battery"
            : state.Snapshot.Batteries.Count > 1
                ? $"Battery Health - {info.DisplayName}"
                : "Battery Health";
        _headline.SetHealth(health, caption);

        RenderCapacityBars(info, status, theme);

        _designRow.Value = BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit);
        _fullRow.Value = BatteryStatusService.Capacity(info.FullChargeCapacity, info.CapacityUnit);
        _wearRow.Value = BatteryStatusService.Wear(health);
        _wearRow.ValueColor = health.IsAvailable ? theme.ForGrade(health.Grade) : null;
        _cycleRow.Value = BatteryStatusService.CycleCount(info.CycleCount);

        _levelRow.Value = BatteryStatusService.Percent(status.ChargePercent);
        _statusRow.Value = BatteryStatusService.ChargeState(status.ChargeState);
        _acRow.Value = BatteryStatusService.AcPower(status.AcPower != AcPowerState.Unknown
            ? status.AcPower
            : state.Snapshot.AcPower);
        _runtimeRow.Value = BatteryStatusService.Runtime(
            status.EstimatedRuntimeSeconds, status.RuntimeIsCalculating);
        _temperatureRow.Value = BatteryStatusService.Temperature(
            status.TemperatureKelvin, state.Settings.TemperatureUnit);
    }

    /// <summary>
    /// Draws the three capacity bars all scaled against design capacity, so the shrinking
    /// full-charge bar reads as degradation at a glance (SRS 9).
    /// </summary>
    private void RenderCapacityBars(BatteryInfo info, BatteryStatus status, Theme theme)
    {
        bool hasDesign = info.DesignCapacity.IsAvailable && info.DesignCapacity.Value > 0;
        _capacityCard.Visible = hasDesign;
        if (!hasDesign) return;

        double design = info.DesignCapacity.Value;

        _designBar.Label = "Design Capacity";
        _designBar.ValueText = BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit);
        _designBar.BarColor = theme.TextMuted;
        _designBar.SetPercent(100, true);

        _fullBar.Label = "Full Charge Capacity";
        _fullBar.ValueText = BatteryStatusService.Capacity(info.FullChargeCapacity, info.CapacityUnit);
        _fullBar.BarColor = theme.Accent;
        _fullBar.SetPercent(
            info.FullChargeCapacity.IsAvailable ? info.FullChargeCapacity.Value / design * 100.0 : 0,
            info.FullChargeCapacity.IsAvailable);

        _currentBar.Label = "Current Capacity";
        _currentBar.ValueText = BatteryStatusService.Capacity(status.RemainingCapacity, info.CapacityUnit);
        _currentBar.BarColor = theme.Excellent;
        _currentBar.SetPercent(
            status.RemainingCapacity.IsAvailable ? status.RemainingCapacity.Value / design * 100.0 : 0,
            status.RemainingCapacity.IsAvailable);
    }

    private void RenderBanner(AppState state, BatteryReading? reading)
    {
        var messages = new List<string>();

        if (reading?.Info.IsConfirmedPeripheral == true)
        {
            messages.Add("This is an attached battery device, not this computer's own battery. "
                         + "Its health does not describe your laptop's pack.");
        }

        if (reading?.Health.IsSuspicious == true && reading.Health.Warning is { } warning)
        {
            messages.Add(warning);
        }

        if (state.ElevationMayHelp)
        {
            // SRS 19: only surfaced when a collection issue was genuinely access-denied.
            messages.Add("Unable to access some battery information. "
                         + "Try running the application as administrator.");
        }

        if (messages.Count == 0)
        {
            _banner.Visible = false;
            return;
        }

        _banner.Message = string.Join("  ", messages);
        _banner.Visible = true;
    }

    private void ApplyTheme(Theme theme)
    {
        _banner.Theme = theme;
        _headlineCard.Theme = theme;
        _headline.Theme = theme;
        _capacityCard.Theme = theme;
        _designBar.Theme = theme;
        _fullBar.Theme = theme;
        _currentBar.Theme = theme;
        _keyFacts.ApplyTheme(theme);
        _liveStatus.ApplyTheme(theme);
        _emptyCard.Theme = theme;
        _emptyTitle.ForeColor = theme.Text;
        _emptyBody.ForeColor = theme.TextMuted;
    }
}
