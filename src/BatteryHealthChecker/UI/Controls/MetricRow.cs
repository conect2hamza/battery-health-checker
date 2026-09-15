using BatteryHealthChecker.Models;

namespace BatteryHealthChecker.UI.Controls;

/// <summary>
/// A label/value pair. Values reading "Not Available" are rendered muted and italic so
/// a missing reading is visually distinct from a real one at a glance (SRS 2, 10).
/// </summary>
public sealed class MetricRow : Control
{
    private Theme _theme = Theme.Light;
    private string _label = string.Empty;
    private string _value = Strings.NotAvailable;
    private Color? _valueColor;

    public MetricRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 28;
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

    public string Value
    {
        get => _value;
        set { _value = value ?? string.Empty; Invalidate(); }
    }

    /// <summary>Overrides the value colour, e.g. to tint a health grade.</summary>
    public Color? ValueColor
    {
        get => _valueColor;
        set { _valueColor = value; Invalidate(); }
    }

    /// <summary>Draws a hairline under the row; off for the last row in a group.</summary>
    public bool ShowSeparator { get; set; } = true;

    /// <summary>
    /// The control lives in an auto-sizing row, so the row asks for a preferred size.
    /// Reporting the explicit height keeps the row from collapsing.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width > 0 ? proposedSize.Width : Width, Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool unavailable = _value == Strings.NotAvailable || _value == Strings.Calculating;
        Color valueColor = _valueColor ?? (unavailable ? _theme.TextMuted : _theme.Text);
        Font valueFont = unavailable ? Fonts.Small : Fonts.Value;

        var labelRect = new Rectangle(0, 0, Width / 2, Height);
        TextRenderer.DrawText(g, _label, Fonts.Base, labelRect, _theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis);

        var valueRect = new Rectangle(Width / 2, 0, Width - Width / 2, Height);
        TextRenderer.DrawText(g, _value, valueFont, valueRect, valueColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis);

        if (ShowSeparator)
        {
            using var pen = new Pen(_theme.Border);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}
