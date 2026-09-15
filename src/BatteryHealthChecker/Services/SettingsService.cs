using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.Services;

/// <summary>User preferences from SRS 22. Every field has a working default.</summary>
public sealed class AppSettings
{
    /// <summary>Refresh intervals offered in the UI (SRS 15).</summary>
    public static readonly int[] AllowedIntervals = { 1, 5, 10, 30, 60 };

    public bool AutoRefresh { get; set; }

    public int RefreshIntervalSeconds { get; set; } = 10;

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public ReportFormat DefaultReportFormat { get; set; } = ReportFormat.Html;

    public string? ReportDirectory { get; set; }

    public bool DebugLogging { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Forces every field back into a valid range. Applied after loading.</summary>
    public void Normalize()
    {
        if (!AllowedIntervals.Contains(RefreshIntervalSeconds)) RefreshIntervalSeconds = 10;
        if (!Enum.IsDefined(TemperatureUnit)) TemperatureUnit = TemperatureUnit.Celsius;
        if (!Enum.IsDefined(Theme)) Theme = ThemeMode.System;
        if (!Enum.IsDefined(DefaultReportFormat)) DefaultReportFormat = ReportFormat.Html;

        if (!string.IsNullOrWhiteSpace(ReportDirectory))
        {
            try
            {
                ReportDirectory = Path.GetFullPath(ReportDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                ReportDirectory = null;
            }
        }
    }

    public string EffectiveReportDirectory =>
        string.IsNullOrWhiteSpace(ReportDirectory) ? AppPaths.DefaultReportDirectory : ReportDirectory!;
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/>.
///
/// SRS 22: no external configuration file is required for the application to start.
/// A missing, unreadable or corrupt settings file is not an error - it yields
/// defaults, and the application runs read-only from that point if the location
/// cannot be written.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly LogService _log;

    public SettingsService(LogService log)
    {
        _log = log;
        Current = new AppSettings();
    }

    public AppSettings Current { get; private set; }

    /// <summary>True when the last save attempt failed, so the UI can say settings will not persist.</summary>
    public bool IsReadOnly { get; private set; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                string json = File.ReadAllText(AppPaths.SettingsFile);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    Current = loaded;
                    return Current;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _log.Warn($"Settings could not be loaded; defaults will be used ({ex.GetType().Name}).");
        }

        Current = new AppSettings();
        return Current;
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Current = settings;

        try
        {
            if (!AppPaths.TryEnsureDirectory(AppPaths.DataDirectory))
            {
                IsReadOnly = true;
                return;
            }

            // Write to a temporary file first so an interrupted save cannot leave a
            // truncated settings file behind.
            string temp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temp, AppPaths.SettingsFile, overwrite: true);
            IsReadOnly = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            IsReadOnly = true;
            _log.Warn($"Settings could not be saved ({ex.GetType().Name}).");
        }
    }
}
