using System.Text;

namespace BatteryHealthChecker.Services;

/// <summary>
/// Optional local-only debug log (SRS 23).
///
/// Off by default, never leaves the machine, and deliberately carries no battery
/// serial numbers or other identifying values - SRS 23 requires logs not to contain
/// unnecessary sensitive information.
/// </summary>
public sealed class LogService
{
    private const long MaxLogBytes = 1024 * 1024;
    private readonly object _gate = new();

    public bool IsEnabled { get; set; }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        if (!IsEnabled) return;

        try
        {
            lock (_gate)
            {
                if (!AppPaths.TryEnsureDirectory(AppPaths.LogDirectory)) return;

                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxLogBytes)
                {
                    // Single roll: keep one previous file so the log cannot grow without bound.
                    string previous = AppPaths.LogFile + ".1";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(AppPaths.LogFile, previous);
                }

                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.LogFile, line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Logging must never be the reason something fails.
        }
    }

    /// <summary>Deletes the log files (SRS 23: Clear Logs). Returns false if anything could not be removed.</summary>
    public bool Clear()
    {
        lock (_gate)
        {
            bool allCleared = true;
            foreach (string path in new[] { AppPaths.LogFile, AppPaths.LogFile + ".1" })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    allCleared = false;
                }
            }
            return allCleared;
        }
    }

    public long CurrentSizeBytes
    {
        get
        {
            try
            {
                var file = new FileInfo(AppPaths.LogFile);
                return file.Exists ? file.Length : 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }
    }
}
