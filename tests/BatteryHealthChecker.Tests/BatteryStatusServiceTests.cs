using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>Display formatting, including the SRS 2 requirement that nothing is invented.</summary>
public class BatteryStatusServiceTests
{
    [Fact]
    public void Missing_readings_format_as_Not_Available_never_as_zero()
    {
        Assert.Equal("Not Available",
            BatteryStatusService.Capacity(Measured<long>.Unavailable(), CapacityUnit.MilliwattHours));
        Assert.Equal("Not Available", BatteryStatusService.Percent(Measured<double>.Unavailable()));
        Assert.Equal("Not Available", BatteryStatusService.Voltage(Measured<int>.Unavailable()));
        Assert.Equal("Not Available", BatteryStatusService.Power(Measured<int>.Unavailable()));
        Assert.Equal("Not Available", BatteryStatusService.CycleCount(Measured<int>.Unavailable()));
        Assert.Equal("Not Available",
            BatteryStatusService.Temperature(Measured<double>.Unavailable(), TemperatureUnit.Celsius));
        Assert.Equal("Not Available", BatteryStatusService.Text(null));
        Assert.Equal("Not Available", BatteryStatusService.Text("   "));
    }

    [Fact]
    public void Absolute_capacities_are_labelled_mWh_with_thousands_separators()
    {
        Measured<long> capacity = Measured<long>.From(60_000, DataSource.BatteryIoctl);

        Assert.Equal("60,000 mWh", BatteryStatusService.Capacity(capacity, CapacityUnit.MilliwattHours));
    }

    [Fact]
    public void Relative_capacities_are_never_labelled_mWh()
    {
        // A device advertising BATTERY_CAPACITY_RELATIVE reports unitless numbers;
        // calling them milliwatt-hours would be inventing a unit (SRS 2).
        Measured<long> capacity = Measured<long>.From(9_000, DataSource.BatteryIoctl);

        string formatted = BatteryStatusService.Capacity(capacity, CapacityUnit.Relative);

        Assert.DoesNotContain("mWh", formatted);
        Assert.Contains("relative", formatted);
    }

    [Theory]
    [InlineData(TemperatureUnit.Celsius, "34.0°C")]
    [InlineData(TemperatureUnit.Fahrenheit, "93.2°F")]
    public void Temperature_converts_from_kelvin_to_the_selected_unit(TemperatureUnit unit, string expected)
    {
        Measured<double> kelvin = Measured<double>.From(307.15, DataSource.BatteryIoctl);

        Assert.Equal(expected, BatteryStatusService.Temperature(kelvin, unit));
    }

    [Fact]
    public void Voltage_is_shown_in_volts()
    {
        Assert.Equal("11.40 V",
            BatteryStatusService.Voltage(Measured<int>.From(11_400, DataSource.BatteryIoctl)));
    }

    [Fact]
    public void Power_flow_names_the_direction_rather_than_showing_a_bare_sign()
    {
        Assert.Contains("charging",
            BatteryStatusService.Power(Measured<int>.From(12_400, DataSource.BatteryIoctl)));
        Assert.Contains("discharging",
            BatteryStatusService.Power(Measured<int>.From(-12_400, DataSource.BatteryIoctl)));
    }

    [Theory]
    [InlineData(11_880, "3h 18m")]
    [InlineData(2_700, "45m")]
    [InlineData(30, "Less than a minute")]
    public void Runtime_formats_as_hours_and_minutes(int seconds, string expected)
    {
        Measured<int> runtime = Measured<int>.From(seconds, DataSource.BatteryIoctl);

        Assert.Equal(expected, BatteryStatusService.Runtime(runtime, isCalculating: false));
    }

    [Fact]
    public void Runtime_distinguishes_calculating_from_unavailable()
    {
        Assert.Equal("Calculating...",
            BatteryStatusService.Runtime(Measured<int>.Unavailable(), isCalculating: true));
        Assert.Equal("Not Available",
            BatteryStatusService.Runtime(Measured<int>.Unavailable(), isCalculating: false));
    }

    [Fact]
    public void An_unrecognised_chemistry_code_is_shown_verbatim_rather_than_as_Other()
    {
        var info = new BatteryInfo { Chemistry = BatteryChemistry.Other, ChemistryRaw = "XYZ1" };

        Assert.Equal("XYZ1", BatteryStatusService.Chemistry(info));
    }

    [Fact]
    public void A_missing_chemistry_is_Not_Available()
    {
        Assert.Equal("Not Available", BatteryStatusService.Chemistry(new BatteryInfo()));
    }
}
