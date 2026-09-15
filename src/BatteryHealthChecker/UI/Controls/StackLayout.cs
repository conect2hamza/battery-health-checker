namespace BatteryHealthChecker.UI.Controls;

/// <summary>
/// A single-column auto-sizing stack.
///
/// WinForms has no vertical stack panel; a one-column <see cref="TableLayoutPanel"/>
/// with auto-sized rows is the standard way to get one that still reflows when the
/// window is resized, which is what SRS 25 needs for small laptop screens.
/// </summary>
public sealed class StackLayout : TableLayoutPanel
{
    public StackLayout()
    {
        ColumnCount = 1;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
    }

    /// <summary>Appends a control as a new auto-sized row that fills the column width.</summary>
    public T Add<T>(T control, int bottomMargin = 14) where T : Control
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 0, bottomMargin);
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(control, 0, RowCount);
        RowCount++;
        return control;
    }
}

/// <summary>A card containing a vertical list of <see cref="MetricRow"/> items.</summary>
public sealed class MetricCard : CardPanel
{
    private readonly List<MetricRow> _rows = new();
    private readonly StackLayout _stack = new();

    public MetricCard(string heading)
    {
        Heading = heading;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(20, 18 + (heading.Length > 0 ? 26 : 0), 20, 14);
        _stack.Dock = DockStyle.Top;
        Controls.Add(_stack);
    }

    /// <summary>Adds a row and returns it so the caller can update its value later.</summary>
    public MetricRow AddRow(string label)
    {
        var row = new MetricRow { Label = label, Theme = Theme, Height = 30 };
        _rows.Add(row);
        _stack.Add(row, bottomMargin: 0);
        UpdateSeparators();
        return row;
    }

    public void ApplyTheme(Theme theme)
    {
        Theme = theme;
        foreach (MetricRow row in _rows) row.Theme = theme;
    }

    private void UpdateSeparators()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].ShowSeparator = i < _rows.Count - 1;
            _rows[i].Invalidate();
        }
    }
}
