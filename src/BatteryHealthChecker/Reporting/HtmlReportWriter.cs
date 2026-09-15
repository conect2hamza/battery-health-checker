using System.Globalization;
using System.Net;
using System.Text;
using BatteryHealthChecker.Models;
using BatteryHealthChecker.Services;

namespace BatteryHealthChecker.Reporting;

/// <summary>
/// HTML report (SRS 16). Entirely self-contained: the CSS is inline and there are no
/// external fonts, scripts or images, so the file opens identically on a machine with
/// no network connection (SRS 17).
/// </summary>
public sealed class HtmlReportWriter : IReportWriter
{
    public ReportFormat Format => ReportFormat.Html;

    public string Extension => "html";

    public string FileDialogFilter => "HTML report (*.html)|*.html";

    public string Render(ReportContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>Battery Health Report - {E(context.System.ComputerName ?? "This PC")}</title>");
        sb.AppendLine($"<style>{Css}</style>");
        sb.AppendLine("</head><body><main>");

        sb.AppendLine("<header class=\"masthead\">");
        sb.AppendLine("<h1>Battery Health Checker Report</h1>");
        sb.AppendLine($"<p class=\"generated\">Generated {E(context.GeneratedAt.ToString("dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture))}</p>");
        sb.AppendLine("</header>");

        RenderSystem(sb, context);

        if (!context.Snapshot.HasBattery)
        {
            sb.AppendLine("<section class=\"card empty\">");
            sb.AppendLine("<h2>No battery detected</h2>");
            sb.AppendLine("<p>This device may be a desktop computer or Windows may be unable to provide battery information.</p>");
            sb.AppendLine("</section>");
        }
        else
        {
            foreach (BatteryReading reading in context.Snapshot.Batteries)
            {
                RenderBattery(sb, context, reading);
            }
        }

        RenderNotes(sb, context);

        sb.AppendLine("<footer>");
        sb.AppendLine($"<p>Battery Health Checker {E(context.ProductVersion)} &middot; Telemetry: Disabled &middot; Cloud Upload: None</p>");
        sb.AppendLine($"<p class=\"disclaimer\">{E(ReportDisclaimer.Text)}</p>");
        sb.AppendLine("</footer>");
        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }

    private static void RenderSystem(StringBuilder sb, ReportContext context)
    {
        sb.AppendLine("<section class=\"card\"><h2>System</h2><dl>");
        Row(sb, "Computer", BatteryStatusService.Text(context.System.ComputerName));
        Row(sb, "Windows Version", BatteryStatusService.Text(context.System.WindowsVersion));
        Row(sb, "Manufacturer", BatteryStatusService.Text(context.System.Manufacturer));
        Row(sb, "Model", BatteryStatusService.Text(context.System.Model));
        Row(sb, "Architecture", BatteryStatusService.Text(context.System.ProcessorArchitecture));
        Row(sb, "AC Power", BatteryStatusService.AcPower(context.Snapshot.AcPower));
        sb.AppendLine("</dl></section>");
    }

    private static void RenderBattery(StringBuilder sb, ReportContext context, BatteryReading reading)
    {
        BatteryInfo info = reading.Info;
        BatteryStatus status = reading.Status;
        HealthResult health = reading.Health;

        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine($"<h2>{E(info.DisplayName)}</h2>");
        sb.AppendLine($"<p class=\"subtitle\">Battery {info.Index + 1} of {context.Snapshot.Batteries.Count}</p>");

        // Headline health figure.
        sb.AppendLine("<div class=\"headline\">");
        if (health.IsAvailable)
        {
            string grade = BatteryHealthCalculator.GradeLabel(health.Grade).ToLowerInvariant();
            sb.AppendLine($"<div class=\"health-value grade-{grade}\">{E(BatteryStatusService.Health(health))}</div>");
            sb.AppendLine($"<div class=\"health-grade grade-{grade}\">{E(BatteryStatusService.Grade(health))}</div>");
            sb.AppendLine($"<div class=\"bar\"><span class=\"fill grade-{grade}\" style=\"width:{health.HealthPercent.ToString("F1", CultureInfo.InvariantCulture)}%\"></span></div>");
        }
        else
        {
            sb.AppendLine($"<div class=\"health-value muted\">{E(Strings.NotAvailable)}</div>");
            sb.AppendLine("<div class=\"health-grade muted\">HEALTH NOT REPORTED</div>");
        }
        sb.AppendLine("</div>");

        if (health.Warning is { } warning)
        {
            sb.AppendLine($"<p class=\"warning\">{E(warning)}</p>");
        }

        // Capacity comparison (SRS 9).
        if (info.DesignCapacity.IsAvailable)
        {
            long design = info.DesignCapacity.Value;
            double fullPercent = info.FullChargeCapacity.IsAvailable && design > 0
                ? Math.Clamp((double)info.FullChargeCapacity.Value / design * 100.0, 0, 100)
                : 0;
            double remainingPercent = status.RemainingCapacity.IsAvailable && design > 0
                ? Math.Clamp((double)status.RemainingCapacity.Value / design * 100.0, 0, 100)
                : 0;

            sb.AppendLine("<h3>Capacity</h3>");
            CapacityBar(sb, "Design Capacity", BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit), 100, "design");
            if (info.FullChargeCapacity.IsAvailable)
            {
                CapacityBar(sb, "Full Charge Capacity",
                    BatteryStatusService.Capacity(info.FullChargeCapacity, info.CapacityUnit), fullPercent, "full");
            }
            if (status.RemainingCapacity.IsAvailable)
            {
                CapacityBar(sb, "Current Capacity",
                    BatteryStatusService.Capacity(status.RemainingCapacity, info.CapacityUnit), remainingPercent, "current");
            }
        }

        sb.AppendLine("<div class=\"columns\">");

        sb.AppendLine("<div><h3>Identification</h3><dl>");
        Row(sb, "Manufacturer", BatteryStatusService.Text(info.Manufacturer));
        Row(sb, "Model", BatteryStatusService.Text(info.Model));
        Row(sb, "Serial Number", BatteryStatusService.Text(info.SerialNumber));
        Row(sb, "Chemistry", BatteryStatusService.Chemistry(info));
        Row(sb, "Manufacture Date", BatteryStatusService.ManufactureDate(info.ManufactureDate));
        sb.AppendLine("</dl></div>");

        sb.AppendLine("<div><h3>Capacity &amp; Health</h3><dl>");
        Row(sb, "Design Capacity", BatteryStatusService.Capacity(info.DesignCapacity, info.CapacityUnit));
        Row(sb, "Full Charge Capacity", BatteryStatusService.Capacity(info.FullChargeCapacity, info.CapacityUnit));
        Row(sb, "Current Capacity", BatteryStatusService.Capacity(status.RemainingCapacity, info.CapacityUnit));
        Row(sb, "Health", BatteryStatusService.Health(health));
        Row(sb, "Wear", BatteryStatusService.Wear(health));
        if (health.IsAvailable)
        {
            Row(sb, "Health (uncapped)", BatteryStatusService.Percent(health.RawHealthPercent, 2));
        }
        sb.AppendLine("</dl></div>");

        sb.AppendLine("<div><h3>Status</h3><dl>");
        Row(sb, "Charge", BatteryStatusService.Percent(status.ChargePercent));
        Row(sb, "Status", BatteryStatusService.ChargeState(status.ChargeState));
        Row(sb, "AC Power", BatteryStatusService.AcPower(status.AcPower));
        Row(sb, "Estimated Runtime",
            BatteryStatusService.Runtime(status.EstimatedRuntimeSeconds, status.RuntimeIsCalculating));
        sb.AppendLine("</dl></div>");

        sb.AppendLine("<div><h3>Additional</h3><dl>");
        Row(sb, "Cycle Count", BatteryStatusService.CycleCount(info.CycleCount));
        Row(sb, "Temperature", BatteryStatusService.Temperature(status.TemperatureKelvin, context.TemperatureUnit));
        Row(sb, "Voltage", BatteryStatusService.Voltage(status.VoltageMillivolts));
        Row(sb, "Design Voltage", BatteryStatusService.Voltage(info.DesignVoltageMillivolts));
        Row(sb, "Power Flow", BatteryStatusService.Power(status.RateMilliwatts));
        Row(sb, "Current", BatteryStatusService.Current(status.CurrentMilliamps));
        sb.AppendLine("</dl></div>");

        sb.AppendLine("</div>");

        sb.AppendLine("<details><summary>Data sources</summary><dl>");
        Row(sb, "Identity and capacity", BatteryStatusService.SourceList(info.ContributingSources));
        Row(sb, "Live status", BatteryStatusService.SourceList(status.ContributingSources));
        sb.AppendLine("</dl></details>");

        sb.AppendLine("</section>");
    }

    private static void RenderNotes(StringBuilder sb, ReportContext context)
    {
        if (context.Snapshot.Issues.Count == 0) return;

        sb.AppendLine("<section class=\"card\"><h2>Notes</h2><ul>");
        foreach (CollectionIssue issue in context.Snapshot.Issues)
        {
            sb.AppendLine($"<li>{E(issue.Message)}</li>");
        }
        sb.AppendLine("</ul></section>");
    }

    private static void CapacityBar(StringBuilder sb, string label, string value, double percent, string kind)
    {
        string width = Math.Clamp(percent, 0, 100).ToString("F1", CultureInfo.InvariantCulture);
        sb.AppendLine("<div class=\"capacity-row\">");
        sb.AppendLine($"<div class=\"capacity-label\"><span>{E(label)}</span><span class=\"capacity-value\">{E(value)}</span></div>");
        sb.AppendLine($"<div class=\"bar\"><span class=\"fill {kind}\" style=\"width:{width}%\"></span></div>");
        sb.AppendLine("</div>");
    }

    private static void Row(StringBuilder sb, string label, string value)
    {
        string cls = value == Strings.NotAvailable ? " class=\"muted\"" : string.Empty;
        sb.AppendLine($"<dt>{E(label)}</dt><dd{cls}>{E(value)}</dd>");
    }

    /// <summary>
    /// HTML-escapes a value. Battery identity strings come from firmware, so they are
    /// treated as untrusted text rather than markup.
    /// </summary>
    private static string E(string value) => WebUtility.HtmlEncode(value);

    private const string Css = """
      :root{color-scheme:light dark;--bg:#f4f6fb;--card:#ffffff;--ink:#101828;--muted:#667085;
        --line:#e4e7ec;--accent:#2563eb;--track:#e9edf5;
        --excellent:#12b76a;--good:#3ba55d;--fair:#f79009;--poor:#f04438;--critical:#b42318;}
      @media (prefers-color-scheme:dark){:root{--bg:#0f1420;--card:#161d2b;--ink:#e6eaf2;
        --muted:#98a2b3;--line:#273245;--track:#1f2938;}}
      *{box-sizing:border-box}
      body{margin:0;background:var(--bg);color:var(--ink);
        font:14px/1.55 "Segoe UI",system-ui,-apple-system,sans-serif;padding:24px 16px}
      main{max-width:940px;margin:0 auto}
      .masthead{margin-bottom:20px}
      h1{font-size:24px;margin:0 0 4px}
      h2{font-size:17px;margin:0 0 2px}
      h3{font-size:12px;letter-spacing:.07em;text-transform:uppercase;color:var(--muted);
        margin:20px 0 8px}
      .generated,.subtitle{color:var(--muted);margin:0 0 14px;font-size:13px}
      .card{background:var(--card);border:1px solid var(--line);border-radius:12px;
        padding:20px 22px;margin-bottom:18px}
      .headline{text-align:center;padding:14px 0 6px}
      .health-value{font-size:52px;font-weight:650;line-height:1.05}
      .health-grade{font-size:13px;letter-spacing:.14em;font-weight:600;margin-top:2px}
      .bar{height:10px;border-radius:999px;background:var(--track);overflow:hidden;margin-top:12px}
      .bar .fill{display:block;height:100%;border-radius:999px;background:var(--accent)}
      .fill.design{background:var(--muted)}
      .fill.full{background:var(--accent)}
      .fill.current{background:var(--excellent)}
      .grade-excellent{color:var(--excellent)} .fill.grade-excellent{background:var(--excellent)}
      .grade-good{color:var(--good)} .fill.grade-good{background:var(--good)}
      .grade-fair{color:var(--fair)} .fill.grade-fair{background:var(--fair)}
      .grade-poor{color:var(--poor)} .fill.grade-poor{background:var(--poor)}
      .grade-critical{color:var(--critical)} .fill.grade-critical{background:var(--critical)}
      .capacity-row{margin-bottom:14px}
      .capacity-label{display:flex;justify-content:space-between;font-size:13px;margin-bottom:5px}
      .capacity-value{color:var(--muted);font-variant-numeric:tabular-nums}
      .columns{display:grid;grid-template-columns:repeat(auto-fit,minmax(250px,1fr));gap:0 28px}
      dl{display:grid;grid-template-columns:auto 1fr;gap:6px 16px;margin:0}
      dt{color:var(--muted);font-size:13px}
      dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
      dd.muted{color:var(--muted);font-style:italic}
      .warning{background:rgba(247,144,9,.12);border-left:3px solid var(--fair);
        padding:9px 12px;border-radius:0 6px 6px 0;font-size:13px;margin:14px 0 0}
      .empty h2{color:var(--muted)}
      details{margin-top:18px;font-size:13px}
      summary{cursor:pointer;color:var(--muted)}
      details dl{margin-top:8px}
      ul{margin:0;padding-left:20px}
      footer{color:var(--muted);font-size:12px;text-align:center;padding:8px 0 20px}
      footer p{margin:0 0 6px}
      .disclaimer{max-width:620px;margin:0 auto}
      @media print{body{background:#fff}.card{break-inside:avoid;border-color:#ccc}}
      """;
}
