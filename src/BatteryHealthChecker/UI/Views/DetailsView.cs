using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI.Controls;

namespace BatteryHealthChecker.UI.Views;

/// <summary>
/// The full technical read-out for the selected battery (SRS 10), including the data
/// sources behind it so an unexpected value can be traced back to where it came from.
/// </summary>
public sealed class DetailsView : ViewBase
{
    private readonly StackLayout _stack = new();
    private readonly MessageBanner _banner = new() { Visible = false };
    private readonly MetricCard _identity = new("Identification");
    private readonly MetricCard _capacity = new("Capacity");
    private readonly MetricCard _electrical = new("Electrical");
    private readonly MetricCard _condition = new("Status and conditions");
    private readonly MetricCard _diagnostics = new("Data sources");

    private readonly MetricRow _name, _manufacturer, _model, _serial, _chemistry, _manufactured, _unique;
    private readonly MetricRow _systemBattery;
    private readonly MetricRow _design, _full, _current, _health, _rawHealth, _wear, _grade, _unit;
    private readonly MetricRow _voltage, _designVoltage, _power, _currentFlow;
    private readonly MetricRow _state, _ac, _cycles, _temperature, _runtime, _critical;
    private readonly MetricRow _identitySources, _statusSources, _deviceKey;

    public DetailsView()
    {
        AutoScroll = true;
        Padding = new Padding(22, 18, 22, 18);

        _name = _identity.AddRow("Battery Name");
        _manufacturer = _identity.AddRow("Manufacturer");
        _model = _identity.AddRow("Model");
        _serial = _identity.AddRow("Serial Number");
        _chemistry = _identity.AddRow("Chemistry");
        _manufactured = _identity.AddRow("Manufacture Date");
        _unique = _identity.AddRow("Unique ID");
        _systemBattery = _identity.AddRow("System Battery");

        _design = _capacity.AddRow("Design Capacity");
        _full = _capacity.AddRow("Full Charge Capacity");
        _current = _capacity.AddRow("Current Capacity");
        _unit = _capacity.AddRow("Capacity Unit");
        _health = _capacity.AddRow("Health");
        _rawHealth = _capacity.AddRow("Health (uncapped)");
        _wear = _capacity.AddRow("Wear");
        _grade = _capacity.AddRow("Condition");

        _voltage = _electrical.AddRow("Voltage");
        _designVoltage = _electrical.AddRow("Design Voltage");
        _power = _electrical.AddRow("Power Flow");
        _currentFlow = _electrical.AddRow("Current");

        _state = _condition.AddRow("Status");
        _ac = _condition.AddRow("AC Power");
        _cycles = _condition.AddRow("Cycle Count");
        _temperature = _condition.AddRow("Temperature");
        _runtime = _condition.AddRow("Estimated Runtime");
        _critical = _condition.AddRow("Critical Level");

        _identitySources = _diagnostics.AddRow("Identity and capacity");
        _statusSources = _diagnostics.AddRow("Live status");
        _deviceKey = _diagnostics.AddRow("Device");

        _stack.Add(_banner);
        _stack.Add(_identity);
        _stack.Add(_capacity);
        _stack.Add(_electrical);
        _stack.Add(_condition);
        _stack.Add(_diagnostics, 4);
        Controls.Add(_stack);
    }

    public override string Title => "Battery Details";

    public override void Render(AppState state)
    {
        Theme theme = state.Theme;
        _banner.Theme = theme;
        foreach (MetricCard card in new[] { _identity, _capacity, _electrical, _condition, _diagnostics })
        {
            card.ApplyTheme(theme);
        }

        BatteryReading? reading = state.SelectedBattery;
        bool hasBattery = reading is not null;

        _identity.Visible = _capacity.Visible = _electrical.Visible
            = _condition.Visible = _diagnostics.Visible = hasBattery;

        if (reading is null)
        {
            _banner.Message = "No battery detected. This device may be a desktop computer or "
                              + "Windows may be unable to provide battery information.";
            _banner.AccentColor = theme.TextMuted;
            _banner.Visible = true;
            return;
        }

        BatteryInfo info = reading.Info;
        BatteryStatus status = reading.Status;
        HealthResult health = reading.Health;

        if (health.Warning is { } warning)
        {
            _banner.Message = warning;
            _banner.AccentColor = health.IsSuspicious ? theme.Fair : theme.TextMuted;
            _banner.Visible = true;
        }
        else
        {
            _banner.Visible = false;
        }

        _name.Value = BatteryStatusService.Text(info.Name);
        _manufacturer.Value = BatteryStatusService.Text(info.Manufacturer);
        _model.Value = BatteryStatusService.Text(info.Model);
        _serial.Value = BatteryStatusService.Text(info.SerialNumber);
        _chemistry.Value = BatteryStatusService.Chemistry(info);
        _manufactured.Value = BatteryStatusService.ManufactureDate(info.ManufactureDate);
        _unique.Value = BatteryStatusService.Text(info.UniqueId);
        _systemBattery.Value = info.IsSystemBattery switch
        {
            true => "Yes - this computer's own battery",
            false => "No - an attached battery device",
            _ => Strings.NotAvailable,
        };
        _systemBattery.ValueColor = info.IsConfirmedPeripheral ? theme.Fair : null;

        _design.Value = BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit);
        _full.Value = BatteryStatusService.Capacity(info.FullChargeCapacity, info.CapacityUnit);
        _current.Value = BatteryStatusService.Capacity(status.RemainingCapacity, info.CapacityUnit);
        _unit.Value = info.CapacityUnit switch
        {
            CapacityUnit.MilliwattHours => "Milliwatt-hours (mWh)",
            // Worth stating explicitly: it explains why no mWh figure is shown (SRS 2).
            CapacityUnit.Relative => "Relative units reported by this battery",
            _ => Strings.NotAvailable,
        };
        _health.Value = BatteryStatusService.Health(health);
        _health.ValueColor = health.IsAvailable ? theme.ForGrade(health.Grade) : null;

        // SRS 6 keeps the uncapped figure for diagnostics; this is where it surfaces.
        _rawHealth.Value = health.IsAvailable
            ? BatteryStatusService.Percent(health.RawHealthPercent, 2)
            : Strings.NotAvailable;
        _wear.Value = BatteryStatusService.Wear(health);
        _grade.Value = BatteryStatusService.Grade(health);
        _grade.ValueColor = health.IsAvailable ? theme.ForGrade(health.Grade) : null;

        _voltage.Value = BatteryStatusService.Voltage(status.VoltageMillivolts);
        _designVoltage.Value = BatteryStatusService.Voltage(info.DesignVoltageMillivolts);
        _power.Value = BatteryStatusService.Power(status.RateMilliwatts);
        _currentFlow.Value = BatteryStatusService.Current(status.CurrentMilliamps);

        _state.Value = BatteryStatusService.ChargeState(status.ChargeState);
        _ac.Value = BatteryStatusService.AcPower(
            status.AcPower != AcPowerState.Unknown ? status.AcPower : state.Snapshot.AcPower);
        _cycles.Value = BatteryStatusService.CycleCount(info.CycleCount);
        _temperature.Value = BatteryStatusService.Temperature(
            status.TemperatureKelvin, state.Settings.TemperatureUnit);
        _runtime.Value = BatteryStatusService.Runtime(
            status.EstimatedRuntimeSeconds, status.RuntimeIsCalculating);
        _critical.Value = status.IsCritical ? "Yes" : "No";
        _critical.ValueColor = status.IsCritical ? theme.Critical : null;

        _identitySources.Value = BatteryStatusService.SourceList(info.ContributingSources);
        _statusSources.Value = BatteryStatusService.SourceList(status.ContributingSources);
        _deviceKey.Value = BatteryStatusService.Text(info.DeviceKey);
    }
}
