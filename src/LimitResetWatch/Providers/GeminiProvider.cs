using System.Management;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LimitResetWatch;

/// <summary>
/// Gemini（Google AI Pro の枠）。Gemini CLI のログインでは AI Pro の枠が返らないので、
/// 起動中の Antigravity（agy.exe）のローカルサーバーに聞く。ポートと CSRF トークンは agy.exe 自身の起動引数にある。
/// </summary>
internal sealed partial class GeminiProvider : IUsageProvider
{
    public ProviderId Id => ProviderId.Gemini;

    sealed record Hub(string Port, string Csrf);

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken ct)
    {
        var hubs = FindHubs();
        var snapshot = new ProviderSnapshot { Id = Id, IsConfigured = hubs.Count > 0 };
        if (hubs.Count == 0)
        {
            return snapshot with { Error = "Antigravity が起動していない。Antigravity（または agy）を起動すると取れる" };
        }

        Exception? lastError = null;
        foreach (var hub in hubs)
        {
            try
            {
                var summary = await RpcAsync(hub, "RetrieveUserQuotaSummary", ct);
                JsonNode? status = null;
                try { status = await RpcAsync(hub, "GetUserStatus", ct); }
                catch (Exception) when (!ct.IsCancellationRequested) { }
                return snapshot with
                {
                    Plan = Json.Str(Json.Get(status, "userStatus", "planStatus", "planInfo", "planName")),
                    Windows = ParseSummary(summary),
                    Source = "Antigravity（agy.exe）の RetrieveUserQuotaSummary",
                    FetchedAt = DateTimeOffset.Now,
                };
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                lastError = error;
            }
        }
        throw lastError!;
    }

    internal static List<UsageWindow> ParseSummary(JsonNode summary)
    {
        var windows = new List<UsageWindow>();
        foreach (var group in Json.Items(Json.Get(summary, "response", "groups")))
        {
            var groupName = Json.Str(Json.Get(group, "displayName")) ?? "";
            foreach (var bucket in Json.Items(Json.Get(group, "buckets")))
            {
                if (Json.Num(Json.Get(bucket, "remainingFraction")) is not { } remaining) continue;
                var window = Json.Str(Json.Get(bucket, "window"));
                var (label, length) = window switch
                {
                    "weekly" => ("週枠", TimeSpan.FromDays(7)),
                    "5h" => ("5時間枠", TimeSpan.FromHours(5)),
                    _ => (window ?? "枠", (TimeSpan?)null),
                };
                windows.Add(new UsageWindow(
                    $"{groupName} {label}".Trim(), (1 - remaining) * 100, Json.Time(Json.Get(bucket, "resetTime")), length));
            }
        }
        return windows;
    }

    static List<Hub> FindHubs()
    {
        var hubs = new List<Hub>();
        using var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name = 'agy.exe'");
        foreach (var process in searcher.Get().OfType<ManagementObject>())
        {
            using (process)
            {
                if (process["CommandLine"] is not string commandLine) continue;
                var port = PortPattern().Match(commandLine);
                var csrf = CsrfPattern().Match(commandLine);
                if (port.Success && csrf.Success) hubs.Add(new Hub(port.Groups[1].Value, csrf.Groups[1].Value.Trim('"')));
            }
        }
        return hubs;
    }

    static Task<JsonNode> RpcAsync(Hub hub, string method, CancellationToken ct) =>
        Http.PostJsonAsync(
            $"http://127.0.0.1:{hub.Port}/exa.language_server_pb.LanguageServerService/{method}", "{}", ct,
            ("x-codeium-csrf-token", hub.Csrf));

    [GeneratedRegex(@"--hub-port[= ](\d+)")]
    private static partial Regex PortPattern();

    [GeneratedRegex(@"--csrf_token[= ](\S+)")]
    private static partial Regex CsrfPattern();
}
