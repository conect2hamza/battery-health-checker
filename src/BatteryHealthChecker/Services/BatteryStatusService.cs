using System.Globalization;
using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Services;

/// <summary>
/// Turns readings into the strings shown in the UI and written into reports (SRS 21:
/// presentation logic lives outside the UI layer).
///
/// Every formatter takes a <see cref="Measured{T}"/> and returns "Not Available" when
/// the reading is missing. That is the single place the SRS 2 rule is enforced for
/// display, so no view can accidentally render a 0 in place of a missing value.
/// </summary>
public static class BatteryStatusService
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static string Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Strings.NotAvailable : value!.Trim();

    /// <summary>
    /// Formats a capacity. Units are only appended when the hardware reports absolute
    /// milliwatt-hours; a device using BATTERY_CAPACITY_RELATIVE reports unitless
    /// numbers and labelling those "mWh" would be inventing a unit (SRS 2).
    /// </summary>
    public static string Capacity(Measured<long> capacity, CapacityUnit unit)
    {
        if (!capacity.IsAvailable) return Strings.NotAvailable;

        string number = capacity.Value.ToString("N0", Culture);
        return unit switch
        {
            CapacityUnit.MilliwattHours => $"{number} mWh",
            CapacityUnit.Relative => $"{number} (relative units)",
            _ => number,
        };
    }

    public static string Percent(Measured<double> percent, int decimals = 0) =>
        percent.IsAvailable ? Percent(percent.Value, decimals) : Strings.NotAvailable;

    public static string Percent(double percent, int decimals = 0) =>
        percent.ToString(decimals > 0 ? $"F{decimals}" : "F0", Culture) + "%";

    public static string Voltage(Measured<int> millivolts) =>
        millivolts.IsAvailable
            ? (millivolts.Value / 1000.0).ToString("F2", Culture) + " V"
            : Strings.NotAvailable;

    /// <summary>Formats the signed power flow, naming the direction rather than showing a bare sign.</summary>
    public static string Power(Measured<int> milliwatts)
    {
        if (!milliwatts.IsAvailable) return Strings.NotAvailable;

        int value = milliwatts.Value;
        if (value == 0) return "0.00 W";

        string magnitude = (Math.Abs(value) / 1000.0).ToString("F2", Culture) + " W";
        return value > 0 ? $"{magnitude} (charging)" : $"{magnitude} (discharging)";
    }

    public static string Current(Measured<double> milliamps)
    {
        if (!milliamps.IsAvailable) return Strings.NotAvailable;

        double value = milliamps.Value;
        string magnitude = (Math.Abs(value) / 1000.0).ToString("F2", Culture) + " A";
        if (Math.Abs(value) < 0.5) return "0.00 A";
        return value > 0 ? $"{magnitude} (charging)" : $"{magnitude} (discharging)";
    }

    /// <summary>Converts from the reported Kelvin to the unit the user selected (SRS 12).</summary>
    public static string Temperature(Measured<double> kelvin, TemperatureUnit unit)
    {
        if (!kelvin.IsAvailable) return Strings.NotAvailable;

        double celsius = kelvin.Value - 273.15;
        return unit == TemperatureUnit.Fahrenheit
            ? (celsius * 9.0 / 5.0 + 32.0).ToString("F1", Culture) + "°F"
            : celsius.ToString("F1", Culture) + "°C";
    }

    /// <summary>
    /// Formats the remaining-runtime estimate (SRS 14). Distinguishes "Windows has not
    /// worked it out yet" from "this machine does not provide it at all".
    /// </summary>
    public static string Runtime(Measured<int> seconds, bool isCalculating)
    {
        if (seconds.IsAvailable)
        {
            TimeSpan span = TimeSpan.FromSeconds(seconds.Value);
            if (span.TotalMinutes < 1) return "Less than a minute";
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : $"{span.Minutes}m";
        }

        return isCalculating ? Strings.Calculating : Strings.NotAvailable;
    }

    public static string CycleCount(Measured<int> cycles) =>
        cycles.IsAvailable ? cycles.Value.ToString("N0", Culture) : Strings.NotAvailable;

    public static string ChargeState(ChargeState state) => state switch
    {
        Models.ChargeState.Charging => "Charging",
        Models.ChargeState.Discharging => "Discharging",
        Models.ChargeState.FullyCharged => "Fully Charged",
        Models.ChargeState.NotCharging => "Not Charging",
        _ => "Unknown",
    };

    public static string AcPower(AcPowerState state) => state switch
    {
        AcPowerState.Connected => "Connected",
        AcPowerState.Disconnected => "Disconnected",
        _ => Strings.NotAvailable,
    };

    public static string Chemistry(BatteryInfo info) => info.Chemistry switch
    {
        BatteryChemistry.LithiumIon => "Lithium-Ion",
        BatteryChemistry.LithiumPolymer => "Lithium-Polymer",
        BatteryChemistry.NickelMetalHydride => "Nickel-Metal Hydride",
        BatteryChemistry.NickelCadmium => "Nickel-Cadmium",
        BatteryChemistry.NickelZinc => "Nickel-Zinc",
        BatteryChemistry.LeadAcid => "Lead-Acid",
        BatteryChemistry.ZincAir => "Zinc-Air",
        BatteryChemistry.RechargeableAlkalineManganese => "Rechargeable Alkaline Manganese",
        // The raw ACPI code is more informative than the word "Other" when we have it.
        BatteryChemistry.Other => Text(info.ChemistryRaw),
        _ => Strings.NotAvailable,
    };

    public static string Health(HealthResult health) =>
        health.IsAvailable ? Percent(health.HealthPercent) : Strings.NotAvailable;

    public static string Wear(HealthResult health) =>
        health.IsAvailable ? Percent(health.WearPercent) : Strings.NotAvailable;

    public static string Grade(HealthResult health) =>
        health.IsAvailable ? BatteryHealthCalculator.GradeLabel(health.Grade) : Strings.NotAvailable;

    public static string ManufactureDate(DateTime? date) =>
        date.HasValue ? date.Value.ToString("dd MMMM yyyy", Culture) : Strings.NotAvailable;

    public static string SourceName(DataSource source) => source switch
    {
        DataSource.BatteryIoctl => "Battery device interface",
        DataSource.WmiAcpi => "ACPI battery classes",
        DataSource.Win32Battery => "Win32_Battery",
        DataSource.SystemPowerStatus => "Windows power API",
        DataSource.Calculated => "Calculated",
        _ => "Unknown",
    };

    /// <summary>Lists the sources that contributed to a reading, for the report's diagnostics.</summary>
    public static string SourceList(IEnumerable<DataSource> sources)
    {
        string joined = string.Join(", ", sources.Distinct().OrderByDescending(s => s).Select(SourceName));
        return joined.Length == 0 ? Strings.NotAvailable : joined;
    }
}
