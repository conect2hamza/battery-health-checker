using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI.Controls;
using BatteryHealthChecker.UI.Views;

namespace BatteryHealthChecker.UI;

/// <summary>
/// Application shell: navigation, the battery selector, refresh control and the view host.
///
/// Scans always run through <see cref="BatteryService.ScanAsync"/> on a background
/// thread and results are marshalled back here, so the window stays responsive while
/// hardware is being queried (SRS 30).
/// </summary>
public sealed class MainForm : Form
{
    private readonly BatteryService _batteryService;
    private readonly SystemInfoService _systemInfoService;
    private readonly SettingsService _settingsService;
    private readonly ReportService _reportService;
    private readonly LogService _log;

    private readonly NavigationRail _nav = new();
    private readonly Panel _content = new();
    private readonly Panel _header = new();
    private readonly Panel _statusBar = new();
    private readonly ComboBox _batterySelector = new();
    private readonly FlatButton _refreshButton = new();
    private readonly CheckBox _autoRefreshToggle = new();
    private readonly Label _headerTitle = new();
    private readonly Label _statusLeft = new();
    private readonly Label _statusRight = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();

    private readonly DashboardView _dashboard;
    private readonly DetailsView _details;
    private readonly ReportsView _reports;
    private readonly SettingsView _settings;
    private readonly AboutView _about;
    private readonly ViewBase[] _views;

    private readonly CancellationTokenSource _shutdown = new();

    private Theme _theme = Theme.Light;
    private BatterySnapshot _snapshot = BatterySnapshot.Empty();
    private int _selectedBattery;
    private bool _scanning;
    private bool _suppressSelectorEvents;
    private DateTime? _lastScanLocal;

    /// <summary>WM_SETTINGCHANGE: used to follow the OS light/dark setting live.</summary>
    private const int WmSettingChange = 0x001A;

    public MainForm(
        BatteryService batteryService,
        SystemInfoService systemInfoService,
        SettingsService settingsService,
        ReportService reportService,
        LogService log)
    {
        _batteryService = batteryService;
        _systemInfoService = systemInfoService;
        _settingsService = settingsService;
        _reportService = reportService;
        _log = log;

        _dashboard = new DashboardView();
        _details = new DetailsView();
        _reports = new ReportsView(reportService);
        _settings = new SettingsView(log);
        _about = new AboutView(batteryService);
        _views = new ViewBase[] { _dashboard, _details, _reports, _settings, _about };

        InitializeShell();
        ApplyTheme(Theme.Resolve(_settingsService.Current.Theme));
    }

    private void InitializeShell()
    {
        Text = "Battery Health Checker";
        MinimumSize = new Size(860, 600);
        Size = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        KeyPreview = true;

        try
        {
            // The icon is embedded in the executable, so there is no external asset (SRS 1).
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A missing icon is cosmetic; never block startup on it.
        }

        _nav.Dock = DockStyle.Left;
        _nav.SetItems(_views.Select(v => v.Title));
        _nav.SelectedIndexChanged += (_, _) => ShowView(_nav.SelectedIndex);

        BuildHeader();
        BuildStatusBar();

        _content.Dock = DockStyle.Fill;
        foreach (ViewBase view in _views)
        {
            view.Visible = false;
            _content.Controls.Add(view);
        }

        _settings.SettingsChanged += OnSettingsChanged;

        // Docking order decides which control owns a full screen edge: the last control
        // added is docked first, so the header spans the whole width, the status bar the
        // whole width below it, and the navigation rail the left side in between.
        Controls.Add(_content);
        Controls.Add(_nav);
        Controls.Add(_statusBar);
        Controls.Add(_header);

        _refreshTimer.Tick += async (_, _) => await RunScanAsync(statusOnly: true);

        Load += async (_, _) =>
        {
            LayoutHeader();
            ShowView(0);
            await RunScanAsync(statusOnly: false);
        };

        FormClosing += (_, _) =>
        {
            _refreshTimer.Stop();
            _shutdown.Cancel();
        };

        KeyDown += async (_, e) =>
        {
            // F5 is the conventional refresh shortcut and costs nothing to support.
            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                await RunScanAsync(statusOnly: false);
            }
        };
    }

    private void BuildHeader()
    {
        _header.Dock = DockStyle.Top;
        _header.Height = 64;

        _headerTitle.Text = "Battery Health Checker";
        _headerTitle.Font = Fonts.CardTitle;
        _headerTitle.AutoSize = true;
        _headerTitle.Location = new Point(24, 22);

        _batterySelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _batterySelector.Font = Fonts.Base;
        _batterySelector.Width = 210;
        _batterySelector.Visible = false;
        _batterySelector.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressSelectorEvents) return;
            _selectedBattery = Math.Max(0, _batterySelector.SelectedIndex);
            RenderActiveView();
        };

        _autoRefreshToggle.Text = "Auto refresh";
        _autoRefreshToggle.Font = Fonts.Base;
        _autoRefreshToggle.AutoSize = true;
        _autoRefreshToggle.CheckedChanged += (_, _) =>
        {
            AppSettings updated = _settingsService.Current.Clone();
            if (updated.AutoRefresh == _autoRefreshToggle.Checked) return;
            updated.AutoRefresh = _autoRefreshToggle.Checked;
            OnSettingsChanged(this, updated);
        };

        _refreshButton.Text = "↻  Refresh";
        _refreshButton.IsPrimary = true;
        _refreshButton.Width = 104;
        _refreshButton.Click += async (_, _) => await RunScanAsync(statusOnly: false);

        _header.Controls.AddRange(new Control[]
        {
            _headerTitle, _batterySelector, _autoRefreshToggle, _refreshButton,
        });
        _header.Resize += (_, _) => LayoutHeader();
    }

    private void LayoutHeader()
    {
        int right = _header.Width - 24;

        _refreshButton.Location = new Point(right - _refreshButton.Width, 16);
        right -= _refreshButton.Width + 16;

        _autoRefreshToggle.Location = new Point(right - _autoRefreshToggle.Width, 22);
        right -= _autoRefreshToggle.Width + 18;

        if (_batterySelector.Visible)
        {
            _batterySelector.Location = new Point(Math.Max(240, right - _batterySelector.Width), 18);
        }
    }

    private void BuildStatusBar()
    {
        _statusBar.Dock = DockStyle.Bottom;
        _statusBar.Height = 30;

        _statusLeft.Font = Fonts.Small;
        _statusLeft.AutoSize = false;
        _statusLeft.Dock = DockStyle.Left;
        _statusLeft.Width = 520;
        _statusLeft.TextAlign = ContentAlignment.MiddleLeft;
        _statusLeft.Padding = new Padding(24, 0, 0, 0);

        _statusRight.Font = Fonts.Small;
        _statusRight.AutoSize = false;
        _statusRight.Dock = DockStyle.Right;
        _statusRight.Width = 300;
        _statusRight.TextAlign = ContentAlignment.MiddleRight;
        _statusRight.Padding = new Padding(0, 0, 24, 0);
        // SRS 18: state the privacy posture where the user can always see it.
        _statusRight.Text = "Telemetry: Disabled   ·   Cloud Upload: None";

        _statusBar.Controls.Add(_statusLeft);
        _statusBar.Controls.Add(_statusRight);
    }

    private void ShowView(int index)
    {
        for (int i = 0; i < _views.Length; i++) _views[i].Visible = i == index;
        RenderActiveView();
    }

    private AppState BuildState() => new()
    {
        Snapshot = _snapshot,
        System = _systemInfoService.Get(),
        Settings = _settingsService.Current,
        Theme = _theme,
        SelectedBatteryIndex = _selectedBattery,
    };

    private void RenderActiveView()
    {
        AppState state = BuildState();
        ViewBase? active = _views.FirstOrDefault(v => v.Visible);

        // Only the visible view is re-rendered; the others refresh when they are shown,
        // which keeps a one-second auto-refresh cheap (SRS 24).
        if (active is null) return;

        active.SuspendLayout();
        try
        {
            active.Render(state);
            if (ReferenceEquals(active, _settings)) _settings.SetPersistenceWarning(_settingsService.IsReadOnly);
        }
        catch (Exception ex)
        {
            _log.Error($"Rendering '{active.Title}' failed.", ex);
        }
        finally
        {
            active.ResumeLayout(performLayout: true);
        }
    }

    private async Task RunScanAsync(bool statusOnly)
    {
        if (_scanning || _shutdown.IsCancellationRequested) return;

        _scanning = true;

        // An auto-refresh tick leaves the button alone; only an explicit scan shows
        // progress, so the control does not flicker once a second.
        if (!statusOnly)
        {
            _refreshButton.Enabled = false;
            _statusLeft.Text = "Reading battery information...";
        }

        try
        {
            BatterySnapshot snapshot = await _batteryService
                .ScanAsync(statusOnly, _shutdown.Token)
                .ConfigureAwait(true);

            // The window can be closed while a scan is in flight - a cold WMI namespace
            // takes the better part of a second, and auto-refresh can fire one every
            // second. Touching controls after that would raise ObjectDisposedException
            // and pop an error dialog after the application has already closed (BUG-003).
            if (IsDisposed || Disposing) return;

            _snapshot = snapshot;
            _lastScanLocal = DateTime.Now;
            UpdateBatterySelector();
            RenderActiveView();
            UpdateStatusText();
        }
        catch (OperationCanceledException)
        {
            // Window is closing.
        }
        catch (Exception ex)
        {
            // A scan must never take the window down (SRS 19).
            _log.Error("Battery scan failed.", ex);
            if (!IsDisposed && !Disposing)
            {
                _statusLeft.Text = "Battery information could not be read. Use Refresh to try again.";
            }
        }
        finally
        {
            _scanning = false;
            if (!IsDisposed && !Disposing) _refreshButton.Enabled = true;
        }
    }

    private void UpdateBatterySelector()
    {
        int count = _snapshot.Batteries.Count;
        _selectedBattery = count == 0 ? 0 : Math.Clamp(_selectedBattery, 0, count - 1);

        // The selector only earns its space on a machine with more than one battery (SRS 11).
        bool show = count > 1;
        if (!show)
        {
            _batterySelector.Visible = false;
            LayoutHeader();
            return;
        }

        _suppressSelectorEvents = true;
        try
        {
            string[] labels = _snapshot.Batteries
                .Select(b => b.Info.IsConfirmedPeripheral
                    ? $"{b.Info.DisplayName} (not this PC's battery)"
                    : $"Battery {b.Info.Index + 1} - {HealthLabel(b)}")
                .ToArray();

            bool changed = _batterySelector.Items.Count != labels.Length
                || !labels.SequenceEqual(_batterySelector.Items.Cast<object>().Select(o => o.ToString()));

            if (changed)
            {
                _batterySelector.Items.Clear();
                _batterySelector.Items.AddRange(labels);
            }

            _batterySelector.SelectedIndex = _selectedBattery;
            _batterySelector.Visible = true;
        }
        finally
        {
            _suppressSelectorEvents = false;
        }

        LayoutHeader();
    }

    private static string HealthLabel(BatteryReading reading) =>
        reading.Health.IsAvailable
            ? $"Health {BatteryStatusService.Health(reading.Health)}"
            : "Health not reported";

    private void UpdateStatusText()
    {
        string timestamp = _lastScanLocal is { } scanned
            ? $"Updated {scanned:HH:mm:ss}"
            : "Not yet updated";

        string batteries = _snapshot.Batteries.Count switch
        {
            0 => "No battery detected",
            1 => "1 battery",
            int n => $"{n} batteries",
        };

        string auto = _settingsService.Current.AutoRefresh
            ? $"   ·   Auto refresh every {_settingsService.Current.RefreshIntervalSeconds}s"
            : string.Empty;

        _statusLeft.Text = $"{timestamp}   ·   {batteries}{auto}";
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _settingsService.Save(settings);
        _log.IsEnabled = settings.DebugLogging;

        _autoRefreshToggle.Checked = settings.AutoRefresh;
        ApplyTheme(Theme.Resolve(settings.Theme));
        ApplyRefreshTimer(settings);
        RenderActiveView();
        UpdateStatusText();
    }

    private void ApplyRefreshTimer(AppSettings settings)
    {
        _refreshTimer.Stop();
        if (!settings.AutoRefresh) return;

        _refreshTimer.Interval = Math.Max(1, settings.RefreshIntervalSeconds) * 1000;
        _refreshTimer.Start();
    }

    private void ApplyTheme(Theme theme)
    {
        _theme = theme;

        BackColor = theme.Background;
        _content.BackColor = theme.Background;
        _header.BackColor = theme.NavBackground;
        _statusBar.BackColor = theme.NavBackground;
        _nav.Theme = theme;
        _refreshButton.Theme = theme;

        _headerTitle.ForeColor = theme.Text;
        _autoRefreshToggle.ForeColor = theme.Text;
        _autoRefreshToggle.BackColor = Color.Transparent;
        _batterySelector.BackColor = theme.Surface;
        _batterySelector.ForeColor = theme.Text;
        _statusLeft.ForeColor = theme.TextMuted;
        _statusRight.ForeColor = theme.TextMuted;

        Invalidate(true);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // Follow the OS theme live while the user has "Use system setting" selected.
        if (m.Msg == WmSettingChange && _settingsService.Current.Theme == ThemeMode.System)
        {
            string? area = m.LParam != IntPtr.Zero
                ? System.Runtime.InteropServices.Marshal.PtrToStringUni(m.LParam)
                : null;

            if (string.Equals(area, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
            {
                ApplyTheme(Theme.Resolve(ThemeMode.System));
                RenderActiveView();
            }
        }
    }

    /// <summary>Applies persisted settings once, at startup.</summary>
    public void ApplyInitialSettings()
    {
        AppSettings settings = _settingsService.Current;
        _log.IsEnabled = settings.DebugLogging;
        _autoRefreshToggle.Checked = settings.AutoRefresh;
        ApplyTheme(Theme.Resolve(settings.Theme));
        ApplyRefreshTimer(settings);
        UpdateStatusText();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _shutdown.Dispose();
        }
        base.Dispose(disposing);
    }
}
