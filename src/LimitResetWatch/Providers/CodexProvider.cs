using System.Text.Json.Nodes;

namespace LimitResetWatch;

/// <summary>Codex（ChatGPT のサブスク）。Codex の /status と同じ値。</summary>
internal sealed class CodexProvider : IUsageProvider
{
    const string UsageUrl = "https://chatgpt.com/backend-api/wham/usage";

    public ProviderId Id => ProviderId.Codex;

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken ct)
    {
        var home = Paths.EnvDir("CODEX_HOME", ".codex");
        var authPath = Path.Combine(home, "auth.json");
        var snapshot = new ProviderSnapshot { Id = Id, IsConfigured = File.Exists(authPath) };
        if (!snapshot.IsConfigured)
        {
            return snapshot with { Error = "Codex のログイン情報（~/.codex/auth.json）が無い。codex でログインする" };
        }

        try
        {
            var tokens = Json.Get(Json.Parse(Paths.ReadShared(authPath)), "tokens");
            var accessToken = Json.Str(Json.Get(tokens, "access_token"))
                ?? throw new InvalidOperationException("auth.json にアクセストークンが無い（ChatGPT でのログインが必要）");
            var headers = new List<(string, string)> { ("Authorization", $"Bearer {accessToken}") };
            if (Json.Str(Json.Get(tokens, "account_id")) is { } accountId) headers.Add(("ChatGPT-Account-Id", accountId));

            var usage = await Http.GetJsonAsync(UsageUrl, ct, [.. headers]);
            var (plan, windows, values) = ParseUsage(usage);
            return snapshot with
            {
                Plan = plan,
                Windows = windows,
                Values = values,
                Source = "chatgpt.com/backend-api/wham/usage",
                FetchedAt = DateTimeOffset.Now,
            };
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            // API が使えないときは、最後に使ったセッションのログに残る値を出す。
            var log = ReadSessionLog(Path.Combine(home, "sessions"));
            if (log is null) throw;
            return snapshot with
            {
                Plan = log.Value.Plan,
                Windows = log.Value.Windows,
                Source = $"セッションログ（{TimeText.At(log.Value.At)} 時点）",
                FetchedAt = log.Value.At,
                Warning = $"API は失敗（{error.Message}）。codex を起動するとログイン情報が更新される",
            };
        }
    }

    internal static string WindowLabel(double seconds)
    {
        var minutes = (int)Math.Round(seconds / 60);
        if (minutes == 300) return "5時間枠";
        if (minutes == 10080) return "週枠";
        return minutes % 1440 == 0 ? $"{minutes / 1440}日枠" : $"{(int)Math.Round(minutes / 60.0)}時間枠";
    }

    internal static (string? Plan, List<UsageWindow> Windows, List<UsageValue> Values) ParseUsage(JsonNode usage)
    {
        var windows = RateLimitRows(Json.Get(usage, "rate_limit"));
        var values = new List<UsageValue>();

        var credits = Json.Get(usage, "credits");
        var unlimited = Json.Bool(Json.Get(credits, "unlimited")) == true;
        if (unlimited || Json.Bool(Json.Get(credits, "has_credits")) == true)
        {
            var balance = Json.Get(credits, "balance")?.ToString() ?? "?";
            values.Add(new UsageValue("クレジット", unlimited ? "無制限" : balance));
        }

        foreach (var extra in Json.Items(Json.Get(usage, "additional_rate_limits")))
        {
            var name = Json.Str(Json.Get(extra, "limit_name")) ?? Json.Str(Json.Get(extra, "limit_id")) ?? "追加";
            foreach (var row in RateLimitRows(Json.Get(extra, "rate_limit") ?? extra))
            {
                windows.Add(row with { Label = $"{name} {row.Label}" });
            }
        }
        return (Json.Str(Json.Get(usage, "plan_type")), windows, values);
    }

    static List<UsageWindow> RateLimitRows(JsonNode? rateLimit)
    {
        var rows = new List<UsageWindow>();
        foreach (var key in new[] { "primary_window", "secondary_window" })
        {
            var window = Json.Get(rateLimit, key);
            if (Json.Num(Json.Get(window, "used_percent")) is not { } used
                || Json.Num(Json.Get(window, "limit_window_seconds")) is not { } seconds) continue;
            rows.Add(new UsageWindow(WindowLabel(seconds), used, Json.Time(Json.Get(window, "reset_at")), TimeSpan.FromSeconds(seconds)));
        }
        return rows;
    }

    static (string? Plan, List<UsageWindow> Windows, DateTimeOffset At)? ReadSessionLog(string root)
    {
        if (!Directory.Exists(root)) return null;
        var files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(10);
        foreach (var file in files)
        {
            foreach (var line in Paths.ReadShared(file.FullName).Split('\n').Reverse())
            {
                if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
                JsonNode? rateLimits;
                try { rateLimits = Json.Get(Json.Parse(line), "payload", "rate_limits"); }
                catch (System.Text.Json.JsonException) { continue; }
                if (rateLimits is null) continue;

                var windows = new List<UsageWindow>();
                foreach (var key in new[] { "primary", "secondary" })
                {
                    var window = Json.Get(rateLimits, key);
                    if (Json.Num(Json.Get(window, "used_percent")) is not { } used
                        || Json.Num(Json.Get(window, "window_minutes")) is not { } minutes) continue;
                    windows.Add(new UsageWindow(
                        WindowLabel(minutes * 60), used, Json.Time(Json.Get(window, "resets_at")), TimeSpan.FromMinutes(minutes)));
                }
                return (Json.Str(Json.Get(rateLimits, "plan_type")), windows, new DateTimeOffset(file.LastWriteTimeUtc));
            }
        }
        return null;
    }
}
