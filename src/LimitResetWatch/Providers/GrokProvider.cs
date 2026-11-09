using System.Text.Json.Nodes;

namespace LimitResetWatch;

/// <summary>Grok（SuperGrok の枠）。Grok Build の /usage と同じ値。</summary>
internal sealed class GrokProvider : IUsageProvider
{
    const string BillingUrl = "https://cli-chat-proxy.grok.com/v1/billing?format=credits";

    public ProviderId Id => ProviderId.Grok;

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken ct)
    {
        var authPath = Path.Combine(Paths.EnvDir("GROK_HOME", ".grok"), "auth.json");
        var snapshot = new ProviderSnapshot { Id = Id, IsConfigured = File.Exists(authPath) };
        if (!snapshot.IsConfigured)
        {
            return snapshot with { Error = "Grok のログイン情報（~/.grok/auth.json）が無い。grok login でサインインする" };
        }

        var login = (Json.Parse(Paths.ReadShared(authPath)) as JsonObject)?
            .Select(pair => pair.Value)
            .FirstOrDefault(value => Json.Str(Json.Get(value, "key")) is not null);
        if (login is null)
        {
            return snapshot with { Error = "~/.grok/auth.json にログイン情報が無い。grok login でサインインする" };
        }
        if (Json.Time(Json.Get(login, "expires_at")) is { } expiresAt && expiresAt < DateTimeOffset.Now)
        {
            return snapshot with
            {
                Error = $"ログインの有効期限が {TimeText.At(expiresAt)} に切れている。grok を起動するとログインが更新される",
            };
        }

        var headers = new List<(string, string)> { ("Authorization", $"Bearer {Json.Str(Json.Get(login, "key"))}") };
        if (Json.Get(login, "user_id")?.ToString() is { } userId) headers.Add(("x-userid", userId));
        var body = await Http.GetJsonAsync(BillingUrl, ct, [.. headers]);
        var (windows, values) = ParseBilling(Json.Get(body, "config"));
        return snapshot with
        {
            Windows = windows,
            Values = values,
            Source = "cli-chat-proxy.grok.com/v1/billing",
            FetchedAt = DateTimeOffset.Now,
        };
    }

    internal static (List<UsageWindow> Windows, List<UsageValue> Values) ParseBilling(JsonNode? config)
    {
        var windows = new List<UsageWindow>();
        var values = new List<UsageValue>();

        var period = Json.Get(config, "currentPeriod");
        var (label, defaultLength) = Json.Str(Json.Get(period, "type")) switch
        {
            "USAGE_PERIOD_TYPE_WEEKLY" => ("週枠", TimeSpan.FromDays(7)),
            "USAGE_PERIOD_TYPE_DAILY" => ("日枠", TimeSpan.FromDays(1)),
            "USAGE_PERIOD_TYPE_MONTHLY" => ("月枠", (TimeSpan?)null),
            _ => ("期間枠", (TimeSpan?)null),
        };
        var end = Json.Time(Json.Get(period, "end"));
        var start = Json.Time(Json.Get(period, "start"));
        var length = start is { } s && end is { } e && e > s ? e - s : defaultLength;

        if (Json.Num(Json.Get(config, "creditUsagePercent")) is { } used)
        {
            windows.Add(new UsageWindow($"{label}（全体）", used, end, length));
        }
        foreach (var product in Json.Items(Json.Get(config, "productUsage")))
        {
            // 製品ごとの値は全体枠のうちの内訳。
            var name = Json.Str(Json.Get(product, "product")) ?? "?";
            var percent = Json.Num(Json.Get(product, "usagePercent"));
            values.Add(new UsageValue($"内訳 {name}", $"うち {percent:0.#}%"));
        }
        if (Json.Num(Json.Get(config, "prepaidBalance", "val")) is { } prepaid && prepaid != 0)
        {
            values.Add(new UsageValue("前払い残高", prepaid.ToString("0.##")));
        }
        return (windows, values);
    }
}
