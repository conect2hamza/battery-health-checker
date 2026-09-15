using BatteryHealthChecker.Models;
using BatteryHealthChecker.Reporting;
using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI.Controls;

namespace BatteryHealthChecker.UI.Views;

/// <summary>
/// Report generation and export (SRS 16).
///
/// The preview is rendered from exactly the same writer that produces the file, so
/// what the user sees is what gets saved.
/// </summary>
public sealed class ReportsView : ViewBase
{
    private readonly ReportService _reportService;
    private readonly StackLayout _stack = new();
    private readonly CardPanel _card = new();
    private readonly ComboBox _formatBox = new();
    private readonly FlatButton _saveAsButton = new();
    private readonly FlatButton _quickSaveButton = new();
    private readonly FlatButton _openFolderButton = new();
    private readonly TextBox _preview = new();
    private readonly Label _formatLabel = new();
    private readonly Label _statusLabel = new();
    private readonly Panel _toolbar = new();

    private AppState? _state;

    public ReportsView(ReportService reportService)
    {
        _reportService = reportService;
        AutoScroll = true;
        Padding = new Padding(22, 18, 22, 18);

        _card.Heading = "Generate a report";
        _card.Subheading = "Reports are created locally. Nothing is uploaded.";
        _card.Padding = new Padding(20, 62, 20, 18);
        _card.Dock = DockStyle.Fill;

        _formatLabel.Text = "Format";
        _formatLabel.Font = Fonts.Base;
        _formatLabel.AutoSize = true;
        _formatLabel.Location = new Point(0, 8);

        _formatBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _formatBox.Font = Fonts.Base;
        _formatBox.Width = 190;
        _formatBox.Location = new Point(56, 4);
        _formatBox.FlatStyle = FlatStyle.Flat;
        foreach (IReportWriter writer in _reportService.Writers)
        {
            _formatBox.Items.Add(new FormatItem(writer));
        }
        _formatBox.SelectedIndexChanged += (_, _) => RefreshPreview();

        _saveAsButton.Text = "Save As...";
        _saveAsButton.IsPrimary = true;
        _saveAsButton.Width = 110;
        _saveAsButton.Location = new Point(262, 2);
        _saveAsButton.Click += (_, _) => SaveAs();

        _quickSaveButton.Text = "Save to default folder";
        _quickSaveButton.Width = 168;
        _quickSaveButton.Location = new Point(382, 2);
        _quickSaveButton.Click += (_, _) => QuickSave();

        _openFolderButton.Text = "Open folder";
        _openFolderButton.Width = 110;
        _openFolderButton.Location = new Point(558, 2);
        _openFolderButton.Click += (_, _) => OpenReportFolder();

        _toolbar.Height = 40;
        _toolbar.Dock = DockStyle.Top;
        _toolbar.BackColor = Color.Transparent;
        _toolbar.Controls.AddRange(new Control[]
        {
            _formatLabel, _formatBox, _saveAsButton, _quickSaveButton, _openFolderButton,
        });

        _statusLabel.Dock = DockStyle.Bottom;
        _statusLabel.Height = 30;
        _statusLabel.Font = Fonts.Small;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        _preview.Multiline = true;
        _preview.ReadOnly = true;
        _preview.ScrollBars = ScrollBars.Vertical;
        _preview.WordWrap = false;
        _preview.Dock = DockStyle.Fill;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Font = new Font(FontFamily.GenericMonospace, 8.5f);

        _card.Controls.Add(_preview);
        _card.Controls.Add(_statusLabel);
        _card.Controls.Add(_toolbar);

        // The preview needs a real height; the stack is auto-size so give the card one.
        _card.Height = 460;
        _card.AutoSize = false;

        _stack.Add(_card, 4);
        Controls.Add(_stack);
    }

    public override string Title => "Reports";

    public override void Render(AppState state)
    {
        bool firstRender = _state is null;
        _state = state;

        Theme theme = state.Theme;
        _card.Theme = theme;
        _saveAsButton.Theme = theme;
        _quickSaveButton.Theme = theme;
        _openFolderButton.Theme = theme;
        _formatLabel.ForeColor = theme.TextMuted;
        _statusLabel.ForeColor = theme.TextMuted;
        _formatBox.BackColor = theme.Surface;
        _formatBox.ForeColor = theme.Text;
        _preview.BackColor = theme.SurfaceAlt;
        _preview.ForeColor = theme.Text;

        if (firstRender || _formatBox.SelectedIndex < 0)
        {
            SelectFormat(state.Settings.DefaultReportFormat);
        }

        RefreshPreview();
    }

    private void SelectFormat(ReportFormat format)
    {
        for (int i = 0; i < _formatBox.Items.Count; i++)
        {
            if (_formatBox.Items[i] is FormatItem item && item.Writer.Format == format)
            {
                _formatBox.SelectedIndex = i;
                return;
            }
        }
        if (_formatBox.Items.Count > 0) _formatBox.SelectedIndex = 0;
    }

    private ReportFormat CurrentFormat =>
        _formatBox.SelectedItem is FormatItem item ? item.Writer.Format : ReportFormat.Html;

    private void RefreshPreview()
    {
        if (_state is null) return;

        try
        {
            ReportContext context = BuildContext();
            string rendered = _reportService.Render(CurrentFormat, context);
            // Normalise line endings so the preview box lays the text out correctly.
            _preview.Text = rendered.ReplaceLineEndings("\r\n");
            _preview.SelectionStart = 0;
            _preview.SelectionLength = 0;
            _statusLabel.Text = $"Preview of {_reportService.BuildFileName(CurrentFormat, DateTime.Now)}";
        }
        catch (Exception ex)
        {
            _preview.Text = string.Empty;
            _statusLabel.Text = $"The report could not be generated: {ex.Message}";
        }
    }

    private ReportContext BuildContext()
    {
        AppState state = _state!;
        return _reportService.BuildContext(state.System, state.Snapshot, state.Settings.TemperatureUnit);
    }

    private void SaveAs()
    {
        if (_state is null) return;

        IReportWriter writer = _reportService.GetWriter(CurrentFormat);
        using var dialog = new SaveFileDialog
        {
            Title = "Save battery report",
            Filter = writer.FileDialogFilter + "|All files (*.*)|*.*",
            FileName = _reportService.BuildFileName(CurrentFormat, DateTime.Now),
            InitialDirectory = _state.Settings.EffectiveReportDirectory,
            // SRS 16: never overwrite an existing report without confirmation.
            OverwritePrompt = true,
            AddExtension = true,
            DefaultExt = writer.Extension,
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        ReportSaveResult result = _reportService.Save(dialog.FileName, CurrentFormat, BuildContext());
        ReportResult(result);
    }

    private void QuickSave()
    {
        if (_state is null) return;

        string directory = _state.Settings.EffectiveReportDirectory;
        string path = Path.Combine(directory, _reportService.BuildFileName(CurrentFormat, DateTime.Now));

        if (File.Exists(path))
        {
            DialogResult confirm = MessageBox.Show(
                FindForm(),
                $"A report named{Environment.NewLine}{Path.GetFileName(path)}{Environment.NewLine}"
                + "already exists in this folder. Replace it?",
                "Replace report?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes) return;
        }

        ReportSaveResult result = _reportService.Save(path, CurrentFormat, BuildContext());
        ReportResult(result);
    }

    private void ReportResult(ReportSaveResult result)
    {
        if (result.Success)
        {
            _statusLabel.Text = $"Saved to {result.Path}";
            return;
        }

        _statusLabel.Text = "The report could not be saved.";
        MessageBox.Show(FindForm(), result.Error ?? "The report could not be saved.",
            "Battery Health Checker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OpenReportFolder()
    {
        if (_state is null) return;

        string directory = _state.Settings.EffectiveReportDirectory;
        try
        {
            if (!Directory.Exists(directory) && !AppPaths.TryEnsureDirectory(directory))
            {
                _statusLabel.Text = "The report folder could not be opened.";
                return;
            }

            // Opens Explorer on the folder. UseShellExecute is required for a directory
            // path, and the path is one the user chose - nothing downloaded or remote.
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true },
            };
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException
                                   or ObjectDisposedException or InvalidOperationException
                                   or UnauthorizedAccessException)
        {
            _statusLabel.Text = "The report folder could not be opened.";
        }
    }

    private sealed record FormatItem(IReportWriter Writer)
    {
        public override string ToString() => Writer.Format switch
        {
            ReportFormat.Html => "HTML (formatted, opens in a browser)",
            ReportFormat.Txt => "Plain text (.txt)",
            ReportFormat.Csv => "CSV (spreadsheet)",
            ReportFormat.Json => "JSON (structured data)",
            _ => Writer.Format.ToString(),
        };
    }
}
