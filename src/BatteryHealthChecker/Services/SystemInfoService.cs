using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using BatteryHealthChecker.Collectors;
using BatteryHealthChecker.Models;
using Microsoft.Win32;

namespace BatteryHealthChecker.Services;

/// <summary>Collects the host details that head a report (SRS 16).</summary>
public sealed class SystemInfoService
{
    private SystemInfo? _cached;

    /// <summary>
    /// System details do not change while the application runs, so they are read once.
    /// Never throws: any field Windows will not answer stays null and renders as
    /// "Not Available".
    /// </summary>
    public SystemInfo Get()
    {
        return _cached ??= Build();
    }

    private static SystemInfo Build()
    {
        string? manufacturer = null;
        string? model = null;

        foreach (ManagementBaseObject obj in WmiHelpers.Query(
                     @"\\.\root\CIMV2",
                     "SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
        {
            manufacturer ??= WmiHelpers.GetString(obj, "Manufacturer");
            model ??= WmiHelpers.GetString(obj, "Model");
            break;
        }

        return new SystemInfo
        {
            ComputerName = SafeMachineName(),
            WindowsVersion = DescribeWindows(),
            WindowsBuild = Environment.OSVersion.Version.Build.ToString(),
            Manufacturer = manufacturer,
            Model = model,
            ProcessorArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            IsElevated = IsElevated(),
        };
    }

    private static string? SafeMachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Produces a human-readable Windows name.
    ///
    /// The registry's ProductName still reads "Windows 10" on Windows 11, so the
    /// edition name is taken from the registry but the generation is decided by the
    /// build number - 22000 is the first Windows 11 build.
    /// </summary>
    private static string DescribeWindows()
    {
        Version version = Environment.OSVersion.Version;
        string generation = version.Build >= 22000 ? "Windows 11" : "Windows 10";

        string? edition = null;
        string? displayVersion = null;
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is not null)
            {
                edition = key.GetValue("ProductName") as string;
                displayVersion = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registry read is best-effort.
        }

        if (!string.IsNullOrWhiteSpace(edition))
        {
            // Rewrite the stale "Windows 10" prefix while keeping the edition suffix.
            int marker = edition!.IndexOf("Windows 1", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                int suffixStart = edition.IndexOf(' ', marker + "Windows 1".Length + 1);
                string suffix = suffixStart > 0 ? edition[suffixStart..] : string.Empty;
                edition = generation + suffix;
            }
        }
        else
        {
            edition = generation;
        }

        string result = edition!.Trim();
        if (!string.IsNullOrWhiteSpace(displayVersion)) result += $" {displayVersion}";
        return $"{result} (build {version.Build})";
    }

    private static bool IsElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
