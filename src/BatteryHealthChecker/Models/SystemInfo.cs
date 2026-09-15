namespace BatteryHealthChecker.Models;

/// <summary>Host details for the report header (SRS 16: SYSTEM section).</summary>
public sealed class SystemInfo
{
    public string? ComputerName { get; init; }
    public string? WindowsVersion { get; init; }
    public string? WindowsBuild { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? ProcessorArchitecture { get; init; }
    public bool IsElevated { get; init; }
}
