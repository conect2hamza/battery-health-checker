using System.Drawing.Drawing2D;

namespace BatteryHealthChecker.UI.Controls;

/// <summary>A rounded surface panel with an optional heading, used for every content block.</summary>
public class CardPanel : Panel
{
    private Theme _theme = Theme.Light;
    private string _heading = string.Empty;
    private string _subheading = string.Empty;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Padding = new Padding(20, 18, 20, 18);
        BackColor = Color.Transparent;
    }

    public Theme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public string Heading
    {
        get => _heading;
        set { _heading = value ?? string.Empty; Invalidate(); }
    }

    public string Subheading
    {
        get => _subheading;
        set { _subheading = value ?? string.Empty; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = Theme.RoundedRect(bounds, 10))
        using (var fill = new SolidBrush(_theme.Surface))
        using (var pen = new Pen(_theme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        if (_heading.Length > 0)
        {
            TextRenderer.DrawText(g, _heading, Fonts.CardTitle,
                new Point(Padding.Left, 14), _theme.Text, TextFormatFlags.NoPrefix);

            if (_subheading.Length > 0)
            {
                TextRenderer.DrawText(g, _subheading, Fonts.Small,
                    new Point(Padding.Left, 36), _theme.TextMuted, TextFormatFlags.NoPrefix);
            }
        }

        base.OnPaint(e);
    }
}
