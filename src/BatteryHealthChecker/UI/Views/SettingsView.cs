using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI.Controls;

namespace BatteryHealthChecker.UI.Views;

/// <summary>Preferences screen (SRS 22).</summary>
public sealed class SettingsView : ViewBase
{
    private readonly LogService _log;
    private readonly StackLayout _stack = new();

    private readonly CardPanel _general = new();
    private readonly CheckBox _autoRefresh = new();
    private readonly ComboBox _interval = new();
    private readonly ComboBox _temperatureUnit = new();
    private readonly Label _intervalLabel = new();
    private readonly Label _temperatureLabel = new();

    private readonly CardPanel _reports = new();
    private readonly ComboBox _reportFormat = new();
    private readonly TextBox _reportPath = new();
    private readonly FlatButton _browse = new();
    private readonly FlatButton _resetPath = new();
    private readonly Label _formatLabel = new();
    private readonly Label _pathLabel = new();

    private readonly CardPanel _appearance = new();
    private readonly ComboBox _theme = new();
    private readonly Label _themeLabel = new();

    private readonly CardPanel _advanced = new();
    private readonly CheckBox _debugLogging = new();
    private readonly FlatButton _clearLogs = new();
    private readonly Label _logStatus = new();
    private readonly Label _persistenceNote = new();

    private AppSettings _settings = new();
    private bool _suppressEvents;

    public SettingsView(LogService log)
    {
        _log = log;
        AutoScroll = true;
        Padding = new Padding(22, 18, 22, 18);

        BuildGeneral();
        BuildReports();
        BuildAppearance();
        BuildAdvanced();

        _stack.Add(_general);
        _stack.Add(_reports);
        _stack.Add(_appearance);
        _stack.Add(_advanced, 4);
        Controls.Add(_stack);
    }

    public override string Title => "Settings";

    /// <summary>Raised whenever the user changes a setting. The host persists and applies it.</summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    private void BuildGeneral()
    {
        _general.Heading = "General";
        _general.Height = 150;
        _general.Padding = new Padding(20, 46, 20, 16);

        _autoRefresh.Text = "Refresh battery information automatically";
        _autoRefresh.Font = Fonts.Base;
        _autoRefresh.AutoSize = true;
        _autoRefresh.Location = new Point(20, 52);
        _autoRefresh.CheckedChanged += (_, _) => Commit(s => s.AutoRefresh = _autoRefresh.Checked);

        _intervalLabel.Text = "Refresh interval";
        _intervalLabel.Font = Fonts.Base;
        _intervalLabel.AutoSize = true;
        _intervalLabel.Location = new Point(20, 86);

        _interval.DropDownStyle = ComboBoxStyle.DropDownList;
        _interval.Font = Fonts.Base;
        _interval.Width = 130;
        _interval.Location = new Point(190, 82);
        foreach (int seconds in AppSettings.AllowedIntervals)
        {
            _interval.Items.Add(seconds == 60 ? "60 seconds" : $"{seconds} second{(seconds == 1 ? "" : "s")}");
        }
        _interval.SelectedIndexChanged += (_, _) => Commit(s =>
            s.RefreshIntervalSeconds = AppSettings.AllowedIntervals[
                Math.Clamp(_interval.SelectedIndex, 0, AppSettings.AllowedIntervals.Length - 1)]);

        _temperatureLabel.Text = "Temperature unit";
        _temperatureLabel.Font = Fonts.Base;
        _temperatureLabel.AutoSize = true;
        _temperatureLabel.Location = new Point(20, 118);

        _temperatureUnit.DropDownStyle = ComboBoxStyle.DropDownList;
        _temperatureUnit.Font = Fonts.Base;
        _temperatureUnit.Width = 130;
        _temperatureUnit.Location = new Point(190, 114);
        _temperatureUnit.Items.AddRange(new object[] { "Celsius (°C)", "Fahrenheit (°F)" });
        _temperatureUnit.SelectedIndexChanged += (_, _) => Commit(s =>
            s.TemperatureUnit = _temperatureUnit.SelectedIndex == 1
                ? TemperatureUnit.Fahrenheit : TemperatureUnit.Celsius);

        _general.Controls.AddRange(new Control[]
        {
            _autoRefresh, _intervalLabel, _interval, _temperatureLabel, _temperatureUnit,
        });
    }

    private void BuildReports()
    {
        _reports.Heading = "Reports";
        _reports.Height = 148;
        _reports.Padding = new Padding(20, 46, 20, 16);

        _formatLabel.Text = "Default format";
        _formatLabel.Font = Fonts.Base;
        _formatLabel.AutoSize = true;
        _formatLabel.Location = new Point(20, 56);

        _reportFormat.DropDownStyle = ComboBoxStyle.DropDownList;
        _reportFormat.Font = Fonts.Base;
        _reportFormat.Width = 130;
        _reportFormat.Location = new Point(190, 52);
        _reportFormat.Items.AddRange(new object[] { "HTML", "Text", "CSV", "JSON" });
        _reportFormat.SelectedIndexChanged += (_, _) => Commit(s =>
            s.DefaultReportFormat = (ReportFormat)Math.Clamp(_reportFormat.SelectedIndex, 0, 3));

        _pathLabel.Text = "Report location";
        _pathLabel.Font = Fonts.Base;
        _pathLabel.AutoSize = true;
        _pathLabel.Location = new Point(20, 94);

        _reportPath.Font = Fonts.Base;
        _reportPath.ReadOnly = true;
        _reportPath.Width = 300;
        _reportPath.Location = new Point(190, 90);
        _reportPath.BorderStyle = BorderStyle.FixedSingle;

        _browse.Text = "Change...";
        _browse.Width = 92;
        _browse.Location = new Point(500, 88);
        _browse.Click += (_, _) => BrowseForFolder();

        _resetPath.Text = "Default";
        _resetPath.Width = 80;
        _resetPath.Location = new Point(600, 88);
        _resetPath.Click += (_, _) => Commit(s => s.ReportDirectory = null);

        _reports.Controls.AddRange(new Control[]
        {
            _formatLabel, _reportFormat, _pathLabel, _reportPath, _browse, _resetPath,
        });
    }

    private void BuildAppearance()
    {
        _appearance.Heading = "Appearance";
        _appearance.Height = 108;
        _appearance.Padding = new Padding(20, 46, 20, 16);

        _themeLabel.Text = "Theme";
        _themeLabel.Font = Fonts.Base;
        _themeLabel.AutoSize = true;
        _themeLabel.Location = new Point(20, 56);

        _theme.DropDownStyle = ComboBoxStyle.DropDownList;
        _theme.Font = Fonts.Base;
        _theme.Width = 130;
        _theme.Location = new Point(190, 52);
        _theme.Items.AddRange(new object[] { "Use system setting", "Light", "Dark" });
        _theme.SelectedIndexChanged += (_, _) => Commit(s =>
            s.Theme = (ThemeMode)Math.Clamp(_theme.SelectedIndex, 0, 2));

        _appearance.Controls.AddRange(new Control[] { _themeLabel, _theme });
    }

    private void BuildAdvanced()
    {
        _advanced.Heading = "Advanced";
        _advanced.Height = 156;
        _advanced.Padding = new Padding(20, 46, 20, 16);

        _debugLogging.Text = "Write a local debug log";
        _debugLogging.Font = Fonts.Base;
        _debugLogging.AutoSize = true;
        _debugLogging.Location = new Point(20, 54);
        _debugLogging.CheckedChanged += (_, _) =>
        {
            Commit(s => s.DebugLogging = _debugLogging.Checked);
            UpdateLogStatus();
        };

        _clearLogs.Text = "Clear logs";
        _clearLogs.Width = 100;
        _clearLogs.Location = new Point(20, 84);
        _clearLogs.Click += (_, _) =>
        {
            bool cleared = _log.Clear();
            _logStatus.Text = cleared
                ? "Logs cleared."
                : "Some log files are in use and could not be removed.";
            UpdateLogStatus(keepMessage: true);
        };

        _logStatus.Font = Fonts.Small;
        _logStatus.AutoSize = false;
        _logStatus.Location = new Point(132, 88);
        _logStatus.Size = new Size(520, 24);

        _persistenceNote.Font = Fonts.Small;
        _persistenceNote.AutoSize = false;
        _persistenceNote.Location = new Point(20, 118);
        _persistenceNote.Size = new Size(620, 30);

        _advanced.Controls.AddRange(new Control[]
        {
            _debugLogging, _clearLogs, _logStatus, _persistenceNote,
        });
    }

    public override void Render(AppState state)
    {
        _settings = state.Settings.Clone();
        Theme theme = state.Theme;

        foreach (CardPanel card in new[] { _general, _reports, _appearance, _advanced }) card.Theme = theme;
        foreach (FlatButton button in new[] { _browse, _resetPath, _clearLogs }) button.Theme = theme;

        foreach (Control control in new Control[]
                 {
                     _intervalLabel, _temperatureLabel, _formatLabel, _pathLabel, _themeLabel,
                 })
        {
            control.ForeColor = theme.TextMuted;
        }

        foreach (CheckBox box in new[] { _autoRefresh, _debugLogging }) box.ForeColor = theme.Text;
        foreach (ComboBox box in new[] { _interval, _temperatureUnit, _reportFormat, _theme })
        {
            box.BackColor = theme.Surface;
            box.ForeColor = theme.Text;
        }

        _reportPath.BackColor = theme.SurfaceAlt;
        _reportPath.ForeColor = theme.Text;
        _logStatus.ForeColor = theme.TextMuted;
        _persistenceNote.ForeColor = theme.TextMuted;

        _suppressEvents = true;
        try
        {
            _autoRefresh.Checked = _settings.AutoRefresh;
            _interval.SelectedIndex = Math.Max(0,
                Array.IndexOf(AppSettings.AllowedIntervals, _settings.RefreshIntervalSeconds));
            _temperatureUnit.SelectedIndex = (int)_settings.TemperatureUnit;
            _reportFormat.SelectedIndex = (int)_settings.DefaultReportFormat;
            _reportPath.Text = _settings.EffectiveReportDirectory;
            _theme.SelectedIndex = (int)_settings.Theme;
            _debugLogging.Checked = _settings.DebugLogging;
        }
        finally
        {
            _suppressEvents = false;
        }

        _interval.Enabled = _settings.AutoRefresh;
        UpdateLogStatus();
    }

    private void UpdateLogStatus(bool keepMessage = false)
    {
        long size = _log.CurrentSizeBytes;

        if (!keepMessage)
        {
            _logStatus.Text = !_debugLogging.Checked
                ? "Logging is off. No diagnostic file is being written."
                : size > 0
                    ? $"Log file: {size / 1024.0:F1} KB in {AppPaths.LogDirectory}"
                    : $"Logging to {AppPaths.LogDirectory}";
        }

        _clearLogs.Enabled = size > 0;
    }

    private void BrowseForFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where reports are saved",
            UseDescriptionForTitle = true,
            SelectedPath = _settings.EffectiveReportDirectory,
        };

        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Commit(s => s.ReportDirectory = dialog.SelectedPath);
        }
    }

    private void Commit(Action<AppSettings> mutate)
    {
        if (_suppressEvents) return;

        AppSettings updated = _settings.Clone();
        mutate(updated);
        updated.Normalize();
        _settings = updated;

        _reportPath.Text = updated.EffectiveReportDirectory;
        _interval.Enabled = updated.AutoRefresh;

        SettingsChanged?.Invoke(this, updated);
    }

    /// <summary>Called by the host when settings could not be written to disk (SRS 22).</summary>
    public void SetPersistenceWarning(bool isReadOnly)
    {
        _persistenceNote.Text = isReadOnly
            ? "Settings cannot be saved on this machine, so they will reset when the application closes. "
              + "The application still works normally."
            : $"Settings are stored per user in {AppPaths.DataDirectory}.";
    }
}
