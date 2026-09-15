using System.Drawing.Drawing2D;
using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.UI.Controls;

/// <summary>
/// A labelled progress bar used both for the headline health figure and for the
/// design-vs-full-charge comparison (SRS 9), which is what makes degradation legible
/// to a non-technical user.
/// </summary>
public sealed class CapacityBar : Control
{
    private Theme _theme = Theme.Light;
    private string _label = string.Empty;
    private string _value = Strings.NotAvailable;
    private double _percent;
    private bool _hasValue;

    public CapacityBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 46;
        BackColor = Color.Transparent;
    }

    public Theme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public string Label
    {
        get => _label;
        set { _label = value ?? string.Empty; Invalidate(); }
    }

    public string ValueText
    {
        get => _value;
        set { _value = value ?? string.Empty; Invalidate(); }
    }

    public Color BarColor { get; set; } = Color.Empty;

    public int BarHeight { get; set; } = 10;

    /// <summary>Sets the fill as a 0-100 percentage of the track.</summary>
    public void SetPercent(double percent, bool hasValue)
    {
        _percent = Math.Clamp(double.IsFinite(percent) ? percent : 0, 0, 100);
        _hasValue = hasValue;
        Invalidate();
    }

    /// <summary>
    /// The control lives in an auto-sizing row, so the row asks for a preferred size.
    /// Reporting the explicit height keeps the row from collapsing.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width > 0 ? proposedSize.Width : Width, Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool unavailable = !_hasValue;

        var labelRect = new Rectangle(0, 0, Width / 2, 20);
        TextRenderer.DrawText(g, _label, Fonts.Base, labelRect, _theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis);

        var valueRect = new Rectangle(Width / 2, 0, Width - Width / 2, 20);
        TextRenderer.DrawText(g, _value, unavailable ? Fonts.Small : Fonts.Value, valueRect,
            unavailable ? _theme.TextMuted : _theme.Text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis);

        int barTop = 24;
        var track = new Rectangle(0, barTop, Math.Max(1, Width - 1), BarHeight);
        using (GraphicsPath trackPath = Theme.RoundedRect(track, BarHeight / 2))
        using (var trackBrush = new SolidBrush(_theme.Track))
        {
            g.FillPath(trackBrush, trackPath);
        }

        if (unavailable || _percent <= 0) return;

        int fillWidth = (int)Math.Round(track.Width * (_percent / 100.0));
        // Keep a sliver visible so a very small non-zero value does not vanish entirely.
        fillWidth = Math.Max(fillWidth, BarHeight);
        fillWidth = Math.Min(fillWidth, track.Width);

        var fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
        Color color = BarColor.IsEmpty ? _theme.Accent : BarColor;
        using GraphicsPath fillPath = Theme.RoundedRect(fill, BarHeight / 2);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, fillPath);
    }
}
