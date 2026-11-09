namespace LimitResetWatch;

internal static class UsageMath
{
    /// <summary>
    /// トレイアイコンに出す枠を選ぶ。リセット前に使い切りたいのは長い枠（週枠など）なので、
    /// まだリセットされていない枠のうち最も長いものを選び、同じ長さが複数あればリセットが早いほうにする。
    /// </summary>
    public static UsageWindow? PickPrimary(IEnumerable<UsageWindow> windows, DateTimeOffset now)
    {
        var withReset = windows.Where(w => w.ResetsAt is not null).ToList();
        if (withReset.Count == 0) return null;
        var upcoming = withReset.Where(w => w.ResetsAt > now).ToList();
        if (upcoming.Count == 0) upcoming = withReset;
        var longest = upcoming.Max(w => w.Length ?? TimeSpan.Zero);
        return upcoming
            .Where(w => (w.Length ?? TimeSpan.Zero) == longest)
            .OrderBy(w => w.ResetsAt)
            .First();
    }

    /// <summary>枠の期間のうち経過した割合（0〜1）。使用率がこれより低ければ、まだ余裕がある。</summary>
    public static double? Elapsed(UsageWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is not { } reset || window.Length is not { } length || length <= TimeSpan.Zero) return null;
        return Math.Clamp(1 - (reset - now) / length, 0, 1);
    }

    /// <summary>
    /// 新しい取得結果を前回の結果に重ねる。取得に失敗して値が無いときは、前回の値を古い値として残す
    /// （リセット時刻は分かっているので、残り時間は数え続けられる）。
    /// </summary>
    public static ProviderSnapshot Merge(ProviderSnapshot? previous, ProviderSnapshot next)
    {
        if (next.HasData || previous is null || !previous.HasData) return next;
        return previous with
        {
            Error = next.Error ?? previous.Error,
            IsStale = true,
            IsConfigured = previous.IsConfigured || next.IsConfigured,
        };
    }

    /// <summary>画面に出すか。ログイン情報があるか、前回までの値が残っていれば出す。</summary>
    public static bool IsVisible(ProviderSnapshot snapshot) => snapshot.IsConfigured || snapshot.HasData;
}
