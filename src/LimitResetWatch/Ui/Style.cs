using System.Drawing.Text;
using Microsoft.Win32;

namespace LimitResetWatch;

internal static class ProviderStyle
{
    public static readonly ProviderId[] Order =
        [ProviderId.Claude, ProviderId.Codex, ProviderId.Gemini, ProviderId.Grok, ProviderId.DeepSeek];

    /// <summary>トレイアイコンを出すサービス。DeepSeek はリセットの無い残高なので出さない。</summary>
    public static bool InTray(ProviderId id) => id != ProviderId.DeepSeek;

    public static string Name(ProviderId id) => id.ToString();

    /// <summary>値がまだ無いときにアイコンに出す略称。</summary>
    public static string Initial(ProviderId id) => id switch
    {
        ProviderId.Claude => "Cl",
        ProviderId.Codex => "Co",
        ProviderId.Gemini => "Ge",
        ProviderId.Grok => "Gr",
        _ => "DS",
    };

    public static Color Accent(ProviderId id, bool light) => id switch
    {
        ProviderId.Claude => Color.FromArgb(0xD9, 0x77, 0x57),
        ProviderId.Codex => light ? Color.FromArgb(0x1F, 0x1F, 0x1F) : Color.FromArgb(0xF2, 0xF2, 0xF2),
        ProviderId.Gemini => Color.FromArgb(0x4C, 0x8D, 0xF6),
        ProviderId.Grok => light ? Color.FromArgb(0x7C, 0x5C, 0xFF) : Color.FromArgb(0x9B, 0x7B, 0xFF),
        _ => Color.FromArgb(0x4D, 0x6B, 0xFE),
    };

    public static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        from.A,
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));
}

internal sealed record Palette(Color Background, Color Text, Color Muted, Color Track, Color Warn, Color Border, bool IsLight)
{
    public static readonly Palette Dark = new(
        Color.FromArgb(0x20, 0x20, 0x20), Color.FromArgb(0xF2, 0xF2, 0xF2), Color.FromArgb(0xA3, 0xA3, 0xA3),
        Color.FromArgb(0x3D, 0x3D, 0x3D), Color.FromArgb(0xE8, 0xA9, 0x3C), Color.FromArgb(0x3A, 0x3A, 0x3A), false);

    public static readonly Palette Light = new(
        Color.FromArgb(0xFA, 0xFA, 0xFA), Color.FromArgb(0x1A, 0x1A, 0x1A), Color.FromArgb(0x66, 0x66, 0x66),
        Color.FromArgb(0xDC, 0xDC, 0xDC), Color.FromArgb(0x9A, 0x5B, 0x00), Color.FromArgb(0xCF, 0xCF, 0xCF), true);
}

/// <summary>Windows の明るい / 暗いテーマ。アプリ（浮かぶ窓）とタスクバー（トレイアイコン）で別に設定できる。</summary>
internal static class Theme
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool AppsLight => Read("AppsUseLightTheme");

    public static bool TaskbarLight => Read("SystemUsesLightTheme");

    static bool Read(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is not int value || value != 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}

internal static class UiFont
{
    public static readonly string Name = Pick("Yu Gothic UI", "Meiryo UI", "Segoe UI");

    static string Pick(params string[] names)
    {
        using var fonts = new InstalledFontCollection();
        var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.FirstOrDefault(installed.Contains) ?? "Segoe UI";
    }
}
