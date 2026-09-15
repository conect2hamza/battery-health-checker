using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;

namespace BatteryHealthChecker.UI;

/// <summary>
/// Everything the views render from. Assembled by <see cref="MainForm"/> after each
/// scan and handed to the views read-only, so no view ever touches hardware (SRS 4, 21).
/// </summary>
public sealed class AppState
{
    public required BatterySnapshot Snapshot { get; init; }
    public required SystemInfo System { get; init; }
    public required AppSettings Settings { get; init; }
    public required Theme Theme { get; init; }

    /// <summary>Which battery the selector has chosen (SRS 11).</summary>
    public int SelectedBatteryIndex { get; init; }

    public BatteryReading? SelectedBattery =>
        SelectedBatteryIndex >= 0 && SelectedBatteryIndex < Snapshot.Batteries.Count
            ? Snapshot.Batteries[SelectedBatteryIndex]
            : null;

    /// <summary>
    /// True when a collection issue was an access failure. Only then may the UI suggest
    /// running as administrator (SRS 19).
    /// </summary>
    public bool ElevationMayHelp =>
        !System.IsElevated && Snapshot.Issues.Any(i => i.ElevationMayHelp);
}

/// <summary>A view that renders <see cref="AppState"/>. Views hold no data of their own.</summary>
public abstract class ViewBase : UserControl
{
    protected ViewBase()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint, true);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.Transparent;
        Dock = DockStyle.Fill;
    }

    /// <summary>Title shown in the navigation rail.</summary>
    public abstract string Title { get; }

    /// <summary>Re-renders from the supplied state. Called on the UI thread after every scan.</summary>
    public abstract void Render(AppState state);
}
