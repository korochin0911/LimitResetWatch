namespace LimitResetWatch;

/// <summary>
/// DeepSeek（従量課金の残高）。リセットの無い残高なので、トレイアイコンは出さず浮かぶ窓にだけ出す。
/// 費用やトークン数の集計は公式 API に無い。
/// </summary>
internal sealed class DeepSeekProvider : IUsageProvider
{
    public ProviderId Id => ProviderId.DeepSeek;

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken ct)
    {
        // エクスプローラーから起動すると、ログイン後に設定した環境変数がプロセスに無いことがあるので、ユーザー環境変数も見る。
        var key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY", EnvironmentVariableTarget.User);
        var snapshot = new ProviderSnapshot { Id = Id, IsConfigured = !string.IsNullOrEmpty(key) };
        if (!snapshot.IsConfigured)
        {
            return snapshot with { Error = "環境変数 DEEPSEEK_API_KEY が無い" };
        }

        var body = await Http.GetJsonAsync("https://api.deepseek.com/user/balance", ct, ("Authorization", $"Bearer {key}"));
        var values = new List<UsageValue>();
        foreach (var info in Json.Items(Json.Get(body, "balance_infos")))
        {
            var currency = Json.Str(Json.Get(info, "currency")) ?? "";
            var total = Json.Get(info, "total_balance")?.ToString() ?? "?";
            values.Add(new UsageValue($"残高 {currency}".Trim(), total));
        }
        return snapshot with
        {
            Plan = Json.Bool(Json.Get(body, "is_available")) == true ? "利用可能" : "残高不足",
            Values = values,
            Source = "api.deepseek.com/user/balance",
            FetchedAt = DateTimeOffset.Now,
        };
    }
}
