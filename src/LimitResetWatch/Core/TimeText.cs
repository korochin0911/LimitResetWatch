using System.Globalization;

namespace LimitResetWatch;

internal static class TimeText
{
    static readonly CultureInfo Ja = CultureInfo.GetCultureInfo("ja-JP");

    /// <summary>「あと3時間12分」「あと4日3時間」の形。</summary>
    public static string Until(DateTimeOffset at, DateTimeOffset now)
    {
        var minutes = (int)Math.Round((at - now).TotalMinutes);
        if (minutes <= 0) return "リセット済み";
        if (minutes < 60) return $"あと{minutes}分";
        var hours = minutes / 60;
        return hours < 48 ? $"あと{hours}時間{minutes % 60}分" : $"あと{hours / 24}日{hours % 24}時間";
    }

    /// <summary>トレイアイコン用の短い形（"45" + "m"、"18" + "h"、"5" + "d"）。</summary>
    public static (string Number, string Unit) Compact(DateTimeOffset at, DateTimeOffset now)
    {
        var left = at - now;
        if (left.TotalMinutes < 59.5) return (Math.Max(0, (int)Math.Round(left.TotalMinutes)).ToString(Ja), "m");
        if (left.TotalHours < 48) return (Math.Max(1, (int)left.TotalHours).ToString(Ja), "h");
        return (((int)left.TotalDays).ToString(Ja), "d");
    }

    /// <summary>「11/13(金) 8:12」の形（ローカル時刻）。</summary>
    public static string At(DateTimeOffset at) => at.ToLocalTime().ToString("M/d(ddd) H:mm", Ja);

    public static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("H:mm", Ja);
}
