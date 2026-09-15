using System.Drawing.Drawing2D;

namespace BatteryHealthChecker.UI.Controls;

/// <summary>A flat themed button, in filled (primary) or outlined (secondary) form.</summary>
public sealed class FlatButton : Control
{
    private Theme _theme = Theme.Light;
    private bool _hovered;
    private bool _pressed;

    public FlatButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                 | ControlStyles.SupportsTransparentBackColor, true);
        Height = 32;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        TabStop = true;
    }

    public Theme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    /// <summary>True for the accent-filled style, false for the outlined style.</summary>
    public bool IsPrimary { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true; Invalidate(); base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); Focus(); }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false; Invalidate(); base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRect(bounds, 6);

        Color background, foreground, border;
        if (!Enabled)
        {
            background = _theme.SurfaceAlt;
            foreground = _theme.TextMuted;
            border = _theme.Border;
        }
        else if (IsPrimary)
        {
            background = _pressed ? Darken(_theme.Accent, 0.82f)
                : _hovered ? Darken(_theme.Accent, 0.92f) : _theme.Accent;
            foreground = _theme.AccentText;
            border = background;
        }
        else
        {
            background = _pressed ? _theme.Track : _hovered ? _theme.SurfaceAlt : _theme.Surface;
            foreground = _theme.Text;
            border = _theme.Border;
        }

        using (var fill = new SolidBrush(background))
        using (var pen = new Pen(border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Text, Fonts.Base, bounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        if (Focused && Enabled)
        {
            var focus = Rectangle.Inflate(bounds, -3, -3);
            using var focusPen = new Pen(foreground) { DashStyle = DashStyle.Dot };
            using GraphicsPath focusPath = Theme.RoundedRect(focus, 4);
            g.DrawPath(focusPen, focusPath);
        }
    }

    private static Color Darken(Color color, float factor) => Color.FromArgb(
        color.A,
        (int)Math.Clamp(color.R * factor, 0, 255),
        (int)Math.Clamp(color.G * factor, 0, 255),
        (int)Math.Clamp(color.B * factor, 0, 255));
}
