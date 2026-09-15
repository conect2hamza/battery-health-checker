using System.Drawing.Drawing2D;

namespace BatteryHealthChecker.UI.Controls;

/// <summary>Left-hand navigation for the sections listed in SRS 25.</summary>
public sealed class NavigationRail : Control
{
    private readonly List<string> _items = new();
    private Theme _theme = Theme.Light;
    private int _selectedIndex;
    private int _hoveredIndex = -1;

    private const int ItemHeight = 40;
    private const int TopPadding = 16;
    private const int SidePadding = 10;

    public NavigationRail()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Width = 178;
        TabStop = true;
    }

    public event EventHandler? SelectedIndexChanged;

    public Theme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = _items.Count == 0 ? 0 : Math.Clamp(value, 0, _items.Count - 1);
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _items.Count - 1));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = IndexAt(e.Y);
        if (index != _hoveredIndex)
        {
            _hoveredIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoveredIndex = -1;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = IndexAt(e.Y);
        if (index >= 0)
        {
            Focus();
            SelectedIndex = index;
        }
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down) { SelectedIndex++; e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { SelectedIndex--; e.Handled = true; }
        base.OnKeyDown(e);
    }

    private int IndexAt(int y)
    {
        int index = (y - TopPadding) / ItemHeight;
        return index >= 0 && index < _items.Count && y >= TopPadding ? index : -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var background = new SolidBrush(_theme.NavBackground))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        using (var borderPen = new Pen(_theme.Border))
        {
            g.DrawLine(borderPen, Width - 1, 0, Width - 1, Height);
        }

        for (int i = 0; i < _items.Count; i++)
        {
            var bounds = new Rectangle(SidePadding, TopPadding + i * ItemHeight,
                Width - SidePadding * 2 - 1, ItemHeight - 4);
            bool selected = i == _selectedIndex;

            if (selected || i == _hoveredIndex)
            {
                using GraphicsPath path = Theme.RoundedRect(bounds, 7);
                using var brush = new SolidBrush(selected ? _theme.NavSelected : _theme.SurfaceAlt);
                g.FillPath(brush, path);
            }

            if (selected)
            {
                // Accent marker on the leading edge of the selected item.
                var marker = new Rectangle(bounds.Left + 2, bounds.Top + 9, 3, bounds.Height - 18);
                using GraphicsPath markerPath = Theme.RoundedRect(marker, 2);
                using var markerBrush = new SolidBrush(_theme.Accent);
                g.FillPath(markerBrush, markerPath);
            }

            var textBounds = new Rectangle(bounds.Left + 16, bounds.Top, bounds.Width - 20, bounds.Height);
            TextRenderer.DrawText(g, _items[i], selected ? Fonts.BaseBold : Fonts.Nav, textBounds,
                selected ? _theme.Text : _theme.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
        }

        if (Focused)
        {
            var focus = new Rectangle(SidePadding, TopPadding + _selectedIndex * ItemHeight,
                Width - SidePadding * 2 - 1, ItemHeight - 4);
            using var pen = new Pen(_theme.Accent) { DashStyle = DashStyle.Dot };
            using GraphicsPath path = Theme.RoundedRect(focus, 7);
            g.DrawPath(pen, path);
        }
    }
}
