using System.Drawing.Drawing2D;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;

namespace BatteryHealthChecker.UI.Controls;

/// <summary>
/// The dashboard's headline: the health percentage, its grade and a proportional bar
/// (SRS 8). When health cannot be calculated it says so plainly instead of showing a
/// zero or an empty gauge (SRS 2, 19).
/// </summary>
public sealed class HealthHeadline : Control
{
    private Theme _theme = Theme.Light;
    private HealthResult _health = HealthResult.Unknown;
    private string _caption = "Battery Health";

    public HealthHeadline()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 210;
        BackColor = Color.Transparent;
    }

    public Theme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public void SetHealth(HealthResult health, string caption)
    {
        _health = health;
        _caption = caption;
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

        int y = 12;
        TextRenderer.DrawText(g, _caption, Fonts.Base, new Rectangle(0, y, Width, 20),
            _theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        y += 24;

        Color gradeColor = _theme.ForGrade(_health.Grade);
        string value = _health.IsAvailable
            ? BatteryStatusService.Percent(_health.HealthPercent)
            : Strings.NotAvailable;

        Font valueFont = _health.IsAvailable ? Fonts.Huge : Fonts.CardTitle;
        TextRenderer.DrawText(g, value, valueFont, new Rectangle(0, y, Width, 70),
            _health.IsAvailable ? gradeColor : _theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        y += _health.IsAvailable ? 74 : 32;

        string grade = _health.IsAvailable
            ? BatteryHealthCalculator.GradeLabel(_health.Grade)
            : "HEALTH NOT REPORTED";
        TextRenderer.DrawText(g, grade, Fonts.Grade, new Rectangle(0, y, Width, 22),
            _health.IsAvailable ? gradeColor : _theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        y += 30;

        int barWidth = Math.Min(Math.Max(Width - 80, 80), 460);
        int barLeft = (Width - barWidth) / 2;
        var track = new Rectangle(barLeft, y, barWidth, 12);

        using (GraphicsPath trackPath = Theme.RoundedRect(track, 6))
        using (var trackBrush = new SolidBrush(_theme.Track))
        {
            g.FillPath(trackBrush, trackPath);
        }

        if (_health.IsAvailable && _health.HealthPercent > 0)
        {
            int width = Math.Max(12, (int)Math.Round(track.Width * (_health.HealthPercent / 100.0)));
            var fill = new Rectangle(track.X, track.Y, Math.Min(width, track.Width), track.Height);
            using GraphicsPath fillPath = Theme.RoundedRect(fill, 6);
            using var brush = new SolidBrush(gradeColor);
            g.FillPath(brush, fillPath);
        }

        y += 22;

        string footnote = _health.IsAvailable
            ? "Estimated from the capacity reported by the battery firmware."
            : _health.Warning ?? "This battery does not report the values needed to calculate health.";
        TextRenderer.DrawText(g, footnote, Fonts.Small,
            new Rectangle(20, y, Width - 40, 34), _theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
