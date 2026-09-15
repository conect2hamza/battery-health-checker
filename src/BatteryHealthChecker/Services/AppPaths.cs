namespace BatteryHealthChecker.Services;

/// <summary>
/// Per-user data locations.
///
/// SRS 22 allows settings to persist in an appropriate per-user Windows location, and
/// SRS 1 forbids any external file being *required*. Both hold here: nothing under
/// these paths has to exist for the application to start, and the directory is only
/// created at the moment something is actually written.
/// </summary>
internal static class AppPaths
{
    public const string ProductName = "BatteryHealthChecker";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        ProductName);

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify),
        ProductName, "logs");

    public static string LogFile => Path.Combine(LogDirectory, "battery-health-checker.log");

    /// <summary>Default export target: the user's Documents folder, falling back to the desktop.</summary>
    public static string DefaultReportDirectory
    {
        get
        {
            string documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify);
            return string.IsNullOrWhiteSpace(documents)
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolderOption.DoNotVerify)
                : documents;
        }
    }

    public static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
