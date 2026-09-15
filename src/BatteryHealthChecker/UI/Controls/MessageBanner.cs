using System.Drawing.Drawing2D;

namespace BatteryHealthChecker.UI.Controls;

/// <summary>
/// An inline notice used for collection problems and unusual readings (SRS 19, 20).
/// Auto-sizes to its text so a long message is never clipped.
/// </summary>
public sealed class MessageBanner : Control
{
    private Theme _theme = Theme.Light;
    private string _message = string.Empty;

    public MessageBanner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Height = 44;
    }

    public Theme Theme
    {
        get => _theme; set { _theme = value; Invalidate(); }
    }

    /// <summary>Accent colour of the left rule; defaults to the theme's "fair" amber.</summary>
    public Color? AccentColor { get; set; }

    public string Message
    {
        get => _message;
        set
        {
            _message = value ?? string.Empty;
            RecalculateHeight();
            Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        RecalculateHeight();
        base.OnResize(e);
    }

    private void RecalculateHeight()
    {
        if (_message.Length == 0 || Width <= 40) return;

        Size measured = TextRenderer.MeasureText(
            _message, Fonts.Base, new Size(Width - 34, int.MaxValue), TextFormatFlags.WordBreak);
        int target = Math.Max(40, measured.Height + 22);
        if (target != Height) Height = target;
    }

    /// <summary>
    /// Height depends on how the message wraps, so the preferred size is measured
    /// against the width the layout is offering rather than the current width.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        if (_message.Length == 0 || width <= 40) return new Size(width, 0);

        Size measured = TextRenderer.MeasureText(
            _message, Fonts.Base, new Size(width - 34, int.MaxValue), TextFormatFlags.WordBreak);
        return new Size(width, Math.Max(40, measured.Height + 22));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_message.Length == 0) return;

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Color accent = AccentColor ?? _theme.Fair;

        using (GraphicsPath path = Theme.RoundedRect(bounds, 7))
        using (var fill = new SolidBrush(_theme.WarningBackground))
        {
            g.FillPath(fill, path);
        }

        using (var rule = new SolidBrush(accent))
        using (GraphicsPath rulePath = Theme.RoundedRect(new Rectangle(0, 4, 4, Height - 9), 2))
        {
            g.FillPath(rule, rulePath);
        }

        TextRenderer.DrawText(g, _message, Fonts.Base,
            new Rectangle(16, 10, Width - 30, Height - 18), _theme.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
