using System.Drawing.Drawing2D;
using BatteryHealthChecker.Models;
using Microsoft.Win32;

namespace BatteryHealthChecker.UI;

/// <summary>Colour palette and shared drawing helpers (SRS 22: Light / Dark / System).</summary>
public sealed class Theme
{
    public required Color Background { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceAlt { get; init; }
    public required Color Border { get; init; }
    public required Color Text { get; init; }
    public required Color TextMuted { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentText { get; init; }
    public required Color Track { get; init; }
    public required Color NavBackground { get; init; }
    public required Color NavSelected { get; init; }
    public required Color WarningBackground { get; init; }

    public required Color Excellent { get; init; }
    public required Color Good { get; init; }
    public required Color Fair { get; init; }
    public required Color Poor { get; init; }
    public required Color Critical { get; init; }

    public required bool IsDark { get; init; }

    public static readonly Theme Light = new()
    {
        Background = Color.FromArgb(0xF4, 0xF6, 0xFB),
        Surface = Color.White,
        SurfaceAlt = Color.FromArgb(0xF8, 0xFA, 0xFD),
        Border = Color.FromArgb(0xE0, 0xE4, 0xEC),
        Text = Color.FromArgb(0x10, 0x18, 0x28),
        TextMuted = Color.FromArgb(0x66, 0x70, 0x85),
        Accent = Color.FromArgb(0x25, 0x63, 0xEB),
        AccentText = Color.White,
        Track = Color.FromArgb(0xE9, 0xED, 0xF5),
        NavBackground = Color.FromArgb(0xFF, 0xFF, 0xFF),
        NavSelected = Color.FromArgb(0xE8, 0xEF, 0xFE),
        WarningBackground = Color.FromArgb(0xFF, 0xF6, 0xE6),
        Excellent = Color.FromArgb(0x12, 0xB7, 0x6A),
        Good = Color.FromArgb(0x35, 0x9E, 0x5A),
        Fair = Color.FromArgb(0xF7, 0x90, 0x09),
        Poor = Color.FromArgb(0xF0, 0x44, 0x38),
        Critical = Color.FromArgb(0xB4, 0x23, 0x18),
        IsDark = false,
    };

    public static readonly Theme Dark = new()
    {
        Background = Color.FromArgb(0x0F, 0x14, 0x20),
        Surface = Color.FromArgb(0x16, 0x1D, 0x2B),
        SurfaceAlt = Color.FromArgb(0x1B, 0x23, 0x33),
        Border = Color.FromArgb(0x27, 0x32, 0x45),
        Text = Color.FromArgb(0xE6, 0xEA, 0xF2),
        TextMuted = Color.FromArgb(0x98, 0xA2, 0xB3),
        Accent = Color.FromArgb(0x60, 0x94, 0xF7),
        AccentText = Color.FromArgb(0x0B, 0x11, 0x1C),
        Track = Color.FromArgb(0x1F, 0x29, 0x38),
        NavBackground = Color.FromArgb(0x13, 0x19, 0x26),
        NavSelected = Color.FromArgb(0x1E, 0x2B, 0x44),
        WarningBackground = Color.FromArgb(0x30, 0x27, 0x14),
        Excellent = Color.FromArgb(0x32, 0xD5, 0x83),
        Good = Color.FromArgb(0x4C, 0xB9, 0x72),
        Fair = Color.FromArgb(0xFD, 0xB0, 0x22),
        Poor = Color.FromArgb(0xF9, 0x70, 0x66),
        Critical = Color.FromArgb(0xE0, 0x4F, 0x40),
        IsDark = true,
    };

    public Color ForGrade(HealthGrade grade) => grade switch
    {
        HealthGrade.Excellent => Excellent,
        HealthGrade.Good => Good,
        HealthGrade.Fair => Fair,
        HealthGrade.Poor => Poor,
        HealthGrade.Critical => Critical,
        _ => TextMuted,
    };

    public static Theme Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Dark => Dark,
        _ => SystemPrefersDark() ? Dark : Light,
    };

    /// <summary>
    /// Reads the Windows app theme preference. A missing value means light, which is
    /// the documented default for machines that have never had the setting changed.
    /// </summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException
                                   or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Builds a rounded-rectangle path. Radius is clamped so it cannot invert the path.</summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int r = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));

        if (r == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        int d = r * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Fonts used across the UI, scaled from the system message-box font for DPI correctness.</summary>
public static class Fonts
{
    private const string Family = "Segoe UI";

    public static Font Base { get; } = Create(9f, FontStyle.Regular);
    public static Font BaseBold { get; } = Create(9f, FontStyle.Bold);
    public static Font Small { get; } = Create(8.25f, FontStyle.Regular);
    public static Font SectionHeading { get; } = Create(8.25f, FontStyle.Bold);
    public static Font CardTitle { get; } = Create(12f, FontStyle.Regular);
    public static Font Nav { get; } = Create(9.75f, FontStyle.Regular);
    public static Font Huge { get; } = Create(46f, FontStyle.Regular);
    public static Font Grade { get; } = Create(11f, FontStyle.Bold);
    public static Font Value { get; } = Create(9.75f, FontStyle.Regular);

    private static Font Create(float size, FontStyle style)
    {
        try
        {
            var font = new Font(Family, size, style, GraphicsUnit.Point);
            // A missing family silently substitutes; check we really got Segoe UI.
            if (string.Equals(font.Name, Family, StringComparison.OrdinalIgnoreCase)) return font;
            font.Dispose();
        }
        catch (ArgumentException)
        {
            // Fall through to the system font below.
        }

        return new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif,
            size, style, GraphicsUnit.Point);
    }
}
