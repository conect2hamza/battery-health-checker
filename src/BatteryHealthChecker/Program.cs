using System.Runtime.InteropServices;
using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI;

namespace BatteryHealthChecker;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // SRS 3: an unsupported environment must be detected gracefully, not crash.
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Battery Health Checker runs on Windows 10 and Windows 11 only.");
            return 1;
        }

        // Applies ApplicationHighDpiMode (PerMonitorV2) from the project file.
        ApplicationConfiguration.Initialize();

        var log = new LogService();
        var settingsService = new SettingsService(log);

        // Settings are loaded before anything else so that debug logging, if the user
        // turned it on last time, captures the rest of startup.
        settingsService.Load();
        log.IsEnabled = settingsService.Current.DebugLogging;
        log.Info($"Starting Battery Health Checker {ReportService.ProductVersion} "
                 + $"on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}).");

        if (!IsSupportedWindowsVersion())
        {
            // Older Windows may still work; warn rather than refuse to start.
            MessageBox.Show(
                "This application is designed for Windows 10 and Windows 11. "
                + "It will still run, but some battery information may be unavailable on this version of Windows.",
                "Battery Health Checker", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // A hardware query that throws on a background thread must not silently kill the
        // process; both handlers convert it into a message the user can act on (SRS 19).
        Application.ThreadException += (_, e) => ReportFatal(log, e.Exception, terminating: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportFatal(log, e.ExceptionObject as Exception, e.IsTerminating);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try
        {
            var batteryService = new BatteryService(log);
            var systemInfoService = new SystemInfoService();
            var reportService = new ReportService(log);

            using var form = new MainForm(
                batteryService, systemInfoService, settingsService, reportService, log);

            form.ApplyInitialSettings();
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            ReportFatal(log, ex, terminating: true);
            return 1;
        }
    }

    private static bool IsSupportedWindowsVersion()
    {
        Version version = Environment.OSVersion.Version;
        return version.Major > 10 || (version.Major == 10 && version.Build >= 10240);
    }

    private static void ReportFatal(LogService log, Exception? exception, bool terminating)
    {
        log.Error("Unhandled exception.", exception);

        string detail = exception?.Message ?? "An unexpected error occurred.";
        string suffix = terminating
            ? Environment.NewLine + Environment.NewLine + "The application will now close."
            : Environment.NewLine + Environment.NewLine + "You can continue using the application.";

        try
        {
            MessageBox.Show(
                "Battery Health Checker ran into a problem:" + Environment.NewLine + Environment.NewLine
                + detail + suffix,
                "Battery Health Checker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            // No message pump available; the log entry above is the record.
        }
    }
}
