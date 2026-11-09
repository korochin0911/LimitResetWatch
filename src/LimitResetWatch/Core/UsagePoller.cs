namespace LimitResetWatch;

internal interface IUsageProvider
{
    ProviderId Id { get; }
    Task<ProviderSnapshot> FetchAsync(CancellationToken ct);
}

internal static class ProviderRegistry
{
    public static IUsageProvider[] CreateAll() =>
        [new ClaudeProvider(), new CodexProvider(), new GeminiProvider(), new GrokProvider(), new DeepSeekProvider()];
}

/// <summary>全サービスの使用量をまとめて取り、前回の値と重ねて持つ。</summary>
internal sealed class UsagePoller(IEnumerable<IUsageProvider> providers, Dictionary<ProviderId, ProviderSnapshot> initial, bool persist)
{
    readonly IUsageProvider[] providers = providers.ToArray();
    readonly Dictionary<ProviderId, ProviderSnapshot> snapshots = new(initial);

    public event Action? Changed;

    public bool IsPolling { get; private set; }

    public DateTimeOffset? LastPollAt { get; private set; }

    public ProviderSnapshot? Get(ProviderId id) => snapshots.GetValueOrDefault(id);

    public IReadOnlyList<ProviderSnapshot> All =>
        ProviderStyle.Order.Select(Get).OfType<ProviderSnapshot>().ToList();

    public async Task PollAsync()
    {
        if (IsPolling) return;
        IsPolling = true;
        Changed?.Invoke();
        try
        {
            var results = await Task.WhenAll(providers.Select(p => Task.Run(() => FetchSafeAsync(p))));
            foreach (var result in results)
            {
                snapshots[result.Id] = UsageMath.Merge(snapshots.GetValueOrDefault(result.Id), result);
            }
            LastPollAt = DateTimeOffset.Now;
            if (persist) SnapshotStore.Save(snapshots.Values);
        }
        finally
        {
            IsPolling = false;
            Changed?.Invoke();
        }
    }

    static async Task<ProviderSnapshot> FetchSafeAsync(IUsageProvider provider)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            return await provider.FetchAsync(cts.Token);
        }
        catch (Exception error)
        {
            return new ProviderSnapshot { Id = provider.Id, IsConfigured = true, Error = $"取得失敗: {error.Message}" };
        }
    }
}
