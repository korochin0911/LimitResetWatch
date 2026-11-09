using System.Text.Json.Nodes;

namespace LimitResetWatch;

/// <summary>Claude（claude.ai のサブスク）。Claude Code の /usage と同じ値。</summary>
internal sealed class ClaudeProvider : IUsageProvider
{
    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    static readonly TimeSpan Session = TimeSpan.FromHours(5);
    static readonly TimeSpan Week = TimeSpan.FromDays(7);

    public ProviderId Id => ProviderId.Claude;

    sealed record Candidate(string Token, DateTimeOffset ExpiresAt, string Source, string? Plan);

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var credentialsPath = Path.Combine(Paths.EnvDir("CLAUDE_CONFIG_DIR", ".claude"), ".credentials.json");
        var cliExists = File.Exists(credentialsPath);
        var desktopExists = ClaudeDesktopCredentials.ConfigExists();
        var snapshot = new ProviderSnapshot { Id = Id, IsConfigured = cliExists || desktopExists };
        if (!snapshot.IsConfigured)
        {
            return snapshot with { Error = "Claude のログイン情報が無い。Claude Code（claude）か Claude デスクトップアプリでログインする" };
        }

        // Claude Code のログインは数時間で切れ、claude を使うまで更新されない。
        // デスクトップアプリのログインはアプリが更新し続けるので、両方を候補にして期限の遅いほうから使う。
        var candidates = new List<Candidate>();
        if (cliExists)
        {
            var oauth = Json.Get(Json.Parse(Paths.ReadShared(credentialsPath)), "claudeAiOauth");
            snapshot = snapshot with
            {
                Plan = JoinPlan(Json.Str(Json.Get(oauth, "subscriptionType")), Json.Str(Json.Get(oauth, "rateLimitTier"))),
            };
            if (Json.Str(Json.Get(oauth, "accessToken")) is { } token
                && Json.Time(Json.Get(oauth, "expiresAt")) is { } expiresAt
                && expiresAt > now.AddMinutes(1))
            {
                candidates.Add(new Candidate(token, expiresAt, "Claude Code", null));
            }
        }
        if (desktopExists)
        {
            candidates.AddRange(ClaudeDesktopCredentials.ReadTokens(now)
                .Select(t => new Candidate(
                    t.AccessToken, t.ExpiresAt, "Claude デスクトップアプリ", JoinPlan(t.SubscriptionType, t.RateLimitTier))));
        }
        if (candidates.Count == 0)
        {
            return snapshot with
            {
                Error = "ログインが切れている。Claude デスクトップアプリを開くか、ターミナルで claude を起動して 1 回やり取りすると更新される",
            };
        }

        HttpStatusException? rejected = null;
        foreach (var candidate in candidates.OrderByDescending(c => c.ExpiresAt))
        {
            try
            {
                var usage = await Http.GetJsonAsync(UsageUrl, ct,
                    ("Authorization", $"Bearer {candidate.Token}"),
                    ("anthropic-beta", "oauth-2025-04-20"));
                return snapshot with
                {
                    Plan = snapshot.Plan ?? candidate.Plan,
                    Windows = ParseUsage(usage),
                    Source = $"api.anthropic.com/api/oauth/usage（{candidate.Source}のログイン）",
                    FetchedAt = DateTimeOffset.Now,
                };
            }
            catch (HttpStatusException error) when (error.StatusCode is 401 or 403)
            {
                rejected = error;
            }
        }
        return snapshot with { Error = $"ログインが受け付けられない（{rejected?.Message}）" };
    }

    static string? JoinPlan(string? subscriptionType, string? rateLimitTier)
    {
        var plan = string.Join(" / ", new[] { subscriptionType, rateLimitTier }.Where(s => !string.IsNullOrEmpty(s)));
        return plan.Length > 0 ? plan : null;
    }

    internal static List<UsageWindow> ParseUsage(JsonNode usage)
    {
        // モデル別の枠（Fable など）は limits にだけ入るので、limits があればそちらを使う。
        var windows = Json.Get(usage, "limits") is JsonArray { Count: > 0 } limits
            ? ParseLimits(usage, limits)
            : ParseLegacy(usage);

        var extra = Json.Get(usage, "extra_usage");
        if (Json.Bool(Json.Get(extra, "is_enabled")) == true && Json.Num(Json.Get(extra, "utilization")) is { } utilization)
        {
            windows.Add(new UsageWindow("追加利用（月）", utilization, null, null));
        }
        return windows;
    }

    static List<UsageWindow> ParseLimits(JsonNode usage, JsonArray limits)
    {
        var windows = new List<UsageWindow>();
        foreach (var limit in limits)
        {
            if (Json.Num(Json.Get(limit, "percent")) is not { } percent) continue;
            var kind = Json.Str(Json.Get(limit, "kind"));
            var group = Json.Str(Json.Get(limit, "group"));
            var scope = Json.Str(Json.Get(limit, "scope", "model", "display_name"))
                ?? Json.Str(Json.Get(limit, "scope", "surface", "display_name"))
                ?? Json.Str(Json.Get(limit, "scope", "surface"));

            string label;
            TimeSpan? length = null;
            if (kind == "session") { label = "5時間枠"; length = Session; }
            else if (kind == "weekly_all") { label = "週枠（全モデル）"; length = Week; }
            else if (group == "weekly") { label = $"週枠（{scope ?? kind}）"; length = Week; }
            else label = scope is null ? kind ?? "枠" : $"{kind}（{scope}）";

            windows.Add(new UsageWindow(label, percent, Json.Time(Json.Get(limit, "resets_at")), length));
        }
        return windows;
    }

    static readonly Dictionary<string, string> LegacyLabels = new()
    {
        ["five_hour"] = "5時間枠",
        ["seven_day"] = "週枠（全モデル）",
        ["seven_day_opus"] = "週枠（Opus）",
        ["seven_day_sonnet"] = "週枠（Sonnet）",
        ["seven_day_oauth_apps"] = "週枠（OAuth アプリ）",
    };

    static List<UsageWindow> ParseLegacy(JsonNode usage)
    {
        var windows = new List<UsageWindow>();
        if (usage is not JsonObject obj) return windows;
        foreach (var (key, value) in obj)
        {
            if (key == "extra_usage" || Json.Num(Json.Get(value, "utilization")) is not { } utilization) continue;
            TimeSpan? length = key == "five_hour" ? Session : key.StartsWith("seven_day", StringComparison.Ordinal) ? Week : null;
            windows.Add(new UsageWindow(
                LegacyLabels.GetValueOrDefault(key, key), utilization, Json.Time(Json.Get(value, "resets_at")), length));
        }
        return windows;
    }
}
