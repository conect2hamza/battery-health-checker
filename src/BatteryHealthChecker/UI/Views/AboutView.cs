using BatteryHealthChecker.Services;
using BatteryHealthChecker.UI.Controls;

namespace BatteryHealthChecker.UI.Views;

/// <summary>About screen (SRS 26) and the privacy statement required by SRS 18.</summary>
public sealed class AboutView : ViewBase
{
    private readonly BatteryService _batteryService;
    private readonly StackLayout _stack = new();
    private readonly CardPanel _about = new();
    private readonly CardPanel _privacy = new();
    private readonly CardPanel _sources = new();

    private readonly Label _title = new();
    private readonly Label _version = new();
    private readonly Label _description = new();
    private readonly Label _badges = new();
    private readonly Label _privacyText = new();
    private readonly Label _sourcesText = new();
    private readonly Label _systemText = new();

    public AboutView(BatteryService batteryService)
    {
        _batteryService = batteryService;
        AutoScroll = true;
        Padding = new Padding(22, 18, 22, 18);

        BuildAbout();
        BuildPrivacy();
        BuildSources();

        _stack.Add(_about);
        _stack.Add(_privacy);
        _stack.Add(_sources, 4);
        Controls.Add(_stack);
    }

    public override string Title => "About";

    private void BuildAbout()
    {
        _about.Height = 196;
        _about.Padding = new Padding(24, 22, 24, 20);

        _title.Text = "Battery Health Checker";
        _title.AutoSize = false;
        _title.Height = 44;
        _title.Dock = DockStyle.Top;
        _title.Font = new Font(Fonts.CardTitle.FontFamily, 18f, FontStyle.Regular);

        _version.Text = $"Version {ReportService.ProductVersion}";
        _version.Font = Fonts.Base;
        _version.AutoSize = false;
        _version.Height = 24;
        _version.Dock = DockStyle.Top;

        _description.Text = "A portable Windows battery diagnostic utility.";
        _description.Font = Fonts.Base;
        _description.AutoSize = false;
        _description.Height = 26;
        _description.Dock = DockStyle.Top;

        // SRS 26: the four claims the About screen must carry.
        _badges.Text = "Offline    ·    Portable    ·    No Installation    ·    No Cloud Upload";
        _badges.Font = Fonts.BaseBold;
        _badges.AutoSize = false;
        _badges.Height = 26;
        _badges.Dock = DockStyle.Top;

        _systemText.Font = Fonts.Small;
        _systemText.AutoSize = false;
        _systemText.Height = 34;
        _systemText.Dock = DockStyle.Top;

        _about.Controls.Add(_systemText);
        _about.Controls.Add(_badges);
        _about.Controls.Add(_description);
        _about.Controls.Add(_version);
        _about.Controls.Add(_title);
    }

    private void BuildPrivacy()
    {
        _privacy.Heading = "Privacy";
        _privacy.Height = 176;
        _privacy.Padding = new Padding(24, 48, 24, 18);

        // SRS 18: state plainly that telemetry and cloud upload are not implemented.
        _privacyText.Text =
            "Telemetry: Disabled" + Environment.NewLine +
            "Cloud Upload: None" + Environment.NewLine + Environment.NewLine +
            "All battery information is read from this computer and stays on it. "
            + "The application makes no network requests, requires no account, installs no "
            + "background service, writes nothing to the registry and changes no Windows, "
            + "BIOS or charging settings. Reports are written only where you choose to save them.";
        _privacyText.Font = Fonts.Base;
        _privacyText.Dock = DockStyle.Fill;
        _privacy.Controls.Add(_privacyText);
    }

    private void BuildSources()
    {
        _sources.Heading = "How the numbers are obtained";
        _sources.Height = 210;
        _sources.Padding = new Padding(24, 48, 24, 18);

        _sourcesText.Font = Fonts.Base;
        _sourcesText.Dock = DockStyle.Fill;
        _sources.Controls.Add(_sourcesText);
    }

    public override void Render(AppState state)
    {
        Theme theme = state.Theme;
        foreach (CardPanel card in new[] { _about, _privacy, _sources }) card.Theme = theme;

        _title.ForeColor = theme.Text;
        _version.ForeColor = theme.TextMuted;
        _description.ForeColor = theme.Text;
        _badges.ForeColor = theme.Accent;
        _systemText.ForeColor = theme.TextMuted;
        _privacyText.ForeColor = theme.Text;
        _sourcesText.ForeColor = theme.Text;

        _systemText.Text = $"Running on {state.System.WindowsVersion ?? "Windows"} "
                           + $"({state.System.ProcessorArchitecture})"
                           + (state.System.IsElevated ? " as administrator." : ".");

        string sources = string.Join(Environment.NewLine,
            _batteryService.CollectorNames.Select(name => "  •  " + name));

        _sourcesText.Text =
            "Battery data is read from the following Windows interfaces, in order of "
            + "preference, and merged so that a value missing from one source can be "
            + "supplied by another:" + Environment.NewLine + Environment.NewLine
            + sources + Environment.NewLine + Environment.NewLine
            + "Health is calculated as (full charge capacity / design capacity) x 100. It is an "
            + "estimate based on the capacity the battery firmware reports, not an absolute "
            + "diagnosis. Values this computer does not report are shown as \"Not Available\" "
            + "rather than guessed.";
    }
}
