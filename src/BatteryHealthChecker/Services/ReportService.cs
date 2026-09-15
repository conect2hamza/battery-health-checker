using System.Globalization;
using System.Reflection;
using System.Text;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Reporting;

namespace BatteryHealthChecker.Services;

/// <summary>Outcome of a save attempt, so the UI can report it without inspecting exceptions.</summary>
/// <param name="Success">True when the file was written.</param>
/// <param name="Path">Where it was written, when successful.</param>
/// <param name="Error">Plain-language failure description otherwise.</param>
public readonly record struct ReportSaveResult(bool Success, string? Path, string? Error)
{
    public static ReportSaveResult Ok(string path) => new(true, path, null);

    public static ReportSaveResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Generates and saves battery reports (SRS 16).
///
/// Overwrite protection is deliberately *not* handled here: the service reports
/// whether a file exists and the caller confirms with the user, which is what SRS 16
/// requires ("do not overwrite an existing report without user confirmation").
/// </summary>
public sealed class ReportService
{
    private readonly LogService _log;
    private readonly IReadOnlyList<IReportWriter> _writers;

    public ReportService(LogService log)
    {
        _log = log;
        _writers = new IReportWriter[]
        {
            new HtmlReportWriter(),
            new TextReportWriter(),
            new CsvReportWriter(),
            new JsonReportWriter(),
        };
    }

    public IReadOnlyList<IReportWriter> Writers => _writers;

    public IReportWriter GetWriter(ReportFormat format) =>
        _writers.FirstOrDefault(w => w.Format == format) ?? _writers[0];

    /// <summary>Product version shown in reports and on the About screen (SRS 26).</summary>
    public static string ProductVersion
    {
        get
        {
            Assembly assembly = typeof(ReportService).Assembly;
            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                // Strip the "+<commit sha>" suffix the SDK appends.
                int plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    public string Render(ReportFormat format, ReportContext context) => GetWriter(format).Render(context);

    /// <summary>
    /// Builds a timestamped file name in the form required by SRS 16, for example
    /// <c>BatteryHealth_2026-09-15_1405.html</c>.
    /// </summary>
    public string BuildFileName(ReportFormat format, DateTime timestamp) =>
        $"BatteryHealth_{timestamp.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture)}"
        + $".{GetWriter(format).Extension}";

    public ReportContext BuildContext(
        SystemInfo system, BatterySnapshot snapshot, TemperatureUnit temperatureUnit) =>
        new(DateTime.Now, system, snapshot, temperatureUnit, ProductVersion);

    public ReportSaveResult Save(string path, ReportFormat format, ReportContext context)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                if (!AppPaths.TryEnsureDirectory(directory))
                {
                    return ReportSaveResult.Fail($"The folder '{directory}' could not be created.");
                }
            }

            string content = Render(format, context);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _log.Info($"Report saved ({format}).");
            return ReportSaveResult.Ok(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or NotSupportedException or ArgumentException or PathTooLongException)
        {
            _log.Error("Report could not be saved.", ex);
            return ReportSaveResult.Fail(ex.Message);
        }
    }
}
