using System.Text.Json.Serialization;

namespace LimitResetWatch;

internal enum ProviderId { Claude, Codex, Gemini, Grok, DeepSeek }

/// <summary>使用率とリセット時刻を持つ 1 つの枠（5 時間枠・週枠など）。</summary>
/// <param name="Length">枠の長さ。リセットまでの経過割合（ペース）の計算に使う。分からなければ null。</param>
internal sealed record UsageWindow(string Label, double UsedPercent, DateTimeOffset? ResetsAt, TimeSpan? Length);

/// <summary>使用率を持たない値（残高・内訳など）。</summary>
internal sealed record UsageValue(string Label, string Value);

/// <summary>1 つのサービスの取得結果。</summary>
internal sealed record ProviderSnapshot
{
    public required ProviderId Id { get; init; }
    public string? Plan { get; init; }
    public IReadOnlyList<UsageWindow> Windows { get; init; } = [];
    public IReadOnlyList<UsageValue> Values { get; init; } = [];

    /// <summary>取得できなかった理由。値が古いまま残っている場合（IsStale）にも入る。</summary>
    public string? Error { get; init; }

    /// <summary>値は取れたが注意が要る場合の補足（API が失敗してログの値を出した、など）。</summary>
    public string? Warning { get; init; }

    public string? Source { get; init; }

    /// <summary>値がいつの時点のものか。</summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary>このサービスのログイン情報などがこの PC にあるか。</summary>
    public bool IsConfigured { get; init; }

    /// <summary>最新の取得に失敗し、前回の値を出しているか。</summary>
    public bool IsStale { get; init; }

    [JsonIgnore]
    public bool HasData => Windows.Count > 0 || Values.Count > 0;
}
