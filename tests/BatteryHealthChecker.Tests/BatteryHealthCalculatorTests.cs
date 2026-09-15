using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>Covers the SRS 6 formula and the SRS 20 validation rules.</summary>
public class BatteryHealthCalculatorTests
{
    private static Measured<long> Capacity(long value) =>
        Measured<long>.From(value, DataSource.BatteryIoctl);

    [Fact]
    public void Calculates_the_worked_example_from_the_specification()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(Capacity(60_000), Capacity(48_000));

        Assert.True(result.IsAvailable);
        Assert.Equal(80.0, result.HealthPercent, 6);
        Assert.Equal(20.0, result.WearPercent, 6);
        Assert.Equal(HealthGrade.Good, result.Grade);
        Assert.False(result.IsSuspicious);
    }

    [Fact]
    public void Wear_is_the_complement_of_health()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(Capacity(60_000), Capacity(52_200));

        Assert.Equal(100.0, result.HealthPercent + result.WearPercent, 6);
        Assert.Equal(87.0, result.HealthPercent, 6);
    }

    [Theory]
    [InlineData(100, HealthGrade.Excellent)]
    [InlineData(90, HealthGrade.Excellent)]
    [InlineData(89.999, HealthGrade.Good)]
    [InlineData(80, HealthGrade.Good)]
    [InlineData(79.999, HealthGrade.Fair)]
    [InlineData(60, HealthGrade.Fair)]
    [InlineData(59.999, HealthGrade.Poor)]
    [InlineData(40, HealthGrade.Poor)]
    [InlineData(39.999, HealthGrade.Critical)]
    [InlineData(0, HealthGrade.Critical)]
    public void Applies_the_specified_classification_thresholds(double health, HealthGrade expected) =>
        Assert.Equal(expected, BatteryHealthCalculator.Classify(health));

    [Fact]
    public void Missing_design_capacity_yields_no_health_rather_than_zero()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(
            Measured<long>.Unavailable(), Capacity(48_000));

        Assert.False(result.IsAvailable);
        Assert.Equal(HealthUnavailableReason.NoDesignCapacity, result.Reason);
        Assert.Equal(HealthGrade.Unknown, result.Grade);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Missing_full_charge_capacity_yields_no_health()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(
            Capacity(60_000), Measured<long>.Unavailable());

        Assert.False(result.IsAvailable);
        Assert.Equal(HealthUnavailableReason.NoFullChargeCapacity, result.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Non_positive_design_capacity_never_divides(long design)
    {
        HealthResult result = BatteryHealthCalculator.Calculate(Capacity(design), Capacity(48_000));

        Assert.False(result.IsAvailable);
        Assert.Equal(HealthUnavailableReason.NonPositiveDesignCapacity, result.Reason);
        Assert.False(double.IsNaN(result.HealthPercent));
        Assert.False(double.IsInfinity(result.HealthPercent));
    }

    [Fact]
    public void Negative_full_charge_capacity_is_rejected()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(Capacity(60_000), Capacity(-5));

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Full_charge_above_design_is_capped_but_flagged_and_the_raw_value_survives()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(Capacity(50_000), Capacity(60_000));

        Assert.True(result.IsAvailable);
        Assert.True(result.IsSuspicious);
        Assert.NotNull(result.Warning);

        // SRS 6: capped for presentation, uncapped preserved for diagnostics.
        Assert.Equal(100.0, result.HealthPercent, 6);
        Assert.Equal(120.0, result.RawHealthPercent, 6);
        Assert.Equal(0.0, result.WearPercent, 6);
    }

    [Fact]
    public void A_fully_worn_battery_reports_zero_health_not_an_error()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(Capacity(60_000), Capacity(0));

        Assert.True(result.IsAvailable);
        Assert.Equal(0.0, result.HealthPercent, 6);
        Assert.Equal(100.0, result.WearPercent, 6);
        Assert.Equal(HealthGrade.Critical, result.Grade);
    }

    [Fact]
    public void Large_capacities_do_not_overflow_or_lose_precision()
    {
        HealthResult result = BatteryHealthCalculator.Calculate(
            Capacity(long.MaxValue / 2), Capacity(long.MaxValue / 4));

        Assert.True(result.IsAvailable);
        Assert.InRange(result.HealthPercent, 49.9, 50.1);
    }
}
