using BatteryHealthChecker.Models;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>
/// The "never fabricate" rule (SRS 2) is enforced by this type, so its behaviour is
/// worth pinning down explicitly.
/// </summary>
public class MeasuredTests
{
    [Fact]
    public void An_unavailable_reading_reports_no_value_and_no_source()
    {
        Measured<long> reading = Measured<long>.Unavailable("firmware did not answer");

        Assert.False(reading.IsAvailable);
        Assert.Equal(DataSource.None, reading.Source);
        Assert.Null(reading.ValueOrNull);
        Assert.Equal(Strings.NotAvailable, reading.ToString());
        Assert.Equal("firmware did not answer", reading.Note);
    }

    [Fact]
    public void Or_prefers_the_more_authoritative_source_when_both_are_available()
    {
        Measured<long> weak = Measured<long>.From(100, DataSource.Win32Battery);
        Measured<long> strong = Measured<long>.From(200, DataSource.BatteryIoctl);

        Assert.Equal(200, weak.Or(strong).Value);
        Assert.Equal(200, strong.Or(weak).Value);
        Assert.Equal(DataSource.BatteryIoctl, weak.Or(strong).Source);
    }

    [Fact]
    public void Or_falls_back_to_whichever_reading_exists()
    {
        Measured<long> missing = Measured<long>.Unavailable();
        Measured<long> present = Measured<long>.From(42, DataSource.WmiAcpi);

        Assert.Equal(42, missing.Or(present).Value);
        Assert.Equal(42, present.Or(missing).Value);
        Assert.False(missing.Or(Measured<long>.Unavailable()).IsAvailable);
    }

    [Fact]
    public void Where_drops_a_reading_that_fails_validation()
    {
        Measured<long> negative = Measured<long>.From(-1, DataSource.WmiAcpi);

        Measured<long> validated = negative.Where(v => v > 0, "not positive");

        Assert.False(validated.IsAvailable);
        Assert.Equal("not positive", validated.Note);
    }

    [Fact]
    public void FromNullable_collapses_null_to_unavailable()
    {
        Assert.False(Measured<int>.FromNullable(null, DataSource.WmiAcpi).IsAvailable);
        Assert.True(Measured<int>.FromNullable(7, DataSource.WmiAcpi).IsAvailable);
    }

    [Fact]
    public void Select_preserves_availability_and_source()
    {
        Measured<int> millivolts = Measured<int>.From(11_400, DataSource.BatteryIoctl);

        Measured<double> volts = millivolts.Select(v => v / 1000.0);

        Assert.True(volts.IsAvailable);
        Assert.Equal(11.4, volts.Value, 6);
        Assert.Equal(DataSource.BatteryIoctl, volts.Source);
        Assert.False(Measured<int>.Unavailable().Select(v => v / 1000.0).IsAvailable);
    }
}
