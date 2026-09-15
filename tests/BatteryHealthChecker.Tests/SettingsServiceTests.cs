using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>Settings normalisation (SRS 22): the application starts with working defaults.</summary>
public class SettingsServiceTests
{
    [Fact]
    public void Defaults_are_usable_without_any_stored_file()
    {
        var settings = new AppSettings();

        Assert.False(settings.AutoRefresh);
        Assert.Contains(settings.RefreshIntervalSeconds, AppSettings.AllowedIntervals);
        Assert.Equal(TemperatureUnit.Celsius, settings.TemperatureUnit);
        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.Equal(ReportFormat.Html, settings.DefaultReportFormat);
        Assert.False(string.IsNullOrWhiteSpace(settings.EffectiveReportDirectory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public void An_out_of_range_interval_is_corrected(int interval)
    {
        var settings = new AppSettings { RefreshIntervalSeconds = interval };

        settings.Normalize();

        Assert.Contains(settings.RefreshIntervalSeconds, AppSettings.AllowedIntervals);
    }

    [Fact]
    public void Undefined_enum_values_from_a_hand_edited_file_are_corrected()
    {
        var settings = new AppSettings
        {
            TemperatureUnit = (TemperatureUnit)99,
            Theme = (ThemeMode)42,
            DefaultReportFormat = (ReportFormat)77,
        };

        settings.Normalize();

        Assert.Equal(TemperatureUnit.Celsius, settings.TemperatureUnit);
        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.Equal(ReportFormat.Html, settings.DefaultReportFormat);
    }

    [Fact]
    public void An_unusable_report_path_falls_back_to_the_default_folder()
    {
        var settings = new AppSettings { ReportDirectory = "\0invalid" };

        settings.Normalize();

        Assert.False(string.IsNullOrWhiteSpace(settings.EffectiveReportDirectory));
    }

    [Fact]
    public void Clone_is_independent_of_the_original()
    {
        var settings = new AppSettings { AutoRefresh = true, RefreshIntervalSeconds = 30 };

        AppSettings copy = settings.Clone();
        copy.AutoRefresh = false;
        copy.RefreshIntervalSeconds = 5;

        Assert.True(settings.AutoRefresh);
        Assert.Equal(30, settings.RefreshIntervalSeconds);
    }
}
