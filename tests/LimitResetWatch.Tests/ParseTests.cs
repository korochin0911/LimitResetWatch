using System.Text.Json.Nodes;

namespace LimitResetWatch.Tests;

public class ParseTests
{
    static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void Claude_limits_use_kind_and_group_for_labels_and_lengths()
    {
        var usage = Parse("""
            {
              "limits": [
                { "kind": "session", "percent": 64, "resets_at": "2026-11-09T15:00:00Z" },
                { "kind": "weekly_all", "group": "weekly", "percent": 82, "resets_at": "2026-11-13T00:00:00Z" },
                { "kind": "weekly_model", "group": "weekly", "percent": 10,
                  "scope": { "model": { "display_name": "Fable" } }, "resets_at": "2026-11-13T00:00:00Z" }
              ],
              "extra_usage": { "is_enabled": true, "utilization": 5 }
            }
            """);

        var windows = ClaudeProvider.ParseUsage(usage);

        Assert.Equal(new[] { "5時間枠", "週枠（全モデル）", "週枠（Fable）", "追加利用（月）" }, windows.Select(w => w.Label));
        Assert.Equal(TimeSpan.FromHours(5), windows[0].Length);
        Assert.Equal(TimeSpan.FromDays(7), windows[2].Length);
        Assert.Equal(82, windows[1].UsedPercent);
        Assert.Null(windows[3].ResetsAt);
    }

    [Fact]
    public void Claude_legacy_format_is_read_when_limits_are_missing()
    {
        var usage = Parse("""
            {
              "five_hour": { "utilization": 30, "resets_at": "2026-11-09T15:00:00Z" },
              "seven_day": { "utilization": 50, "resets_at": "2026-11-13T00:00:00Z" },
              "seven_day_opus": null
            }
            """);

        var windows = ClaudeProvider.ParseUsage(usage);

        Assert.Equal(new[] { "5時間枠", "週枠（全モデル）" }, windows.Select(w => w.Label));
        Assert.Equal(TimeSpan.FromDays(7), windows[1].Length);
    }

    [Fact]
    public void Codex_windows_credits_and_additional_limits()
    {
        var usage = Parse("""
            {
              "plan_type": "pro",
              "rate_limit": {
                "primary_window": { "used_percent": 12, "limit_window_seconds": 18000, "reset_at": 1762700000 },
                "secondary_window": { "used_percent": 18, "limit_window_seconds": 604800, "reset_at": 1763075526 }
              },
              "credits": { "has_credits": true, "balance": "12.5" },
              "additional_rate_limits": [
                { "limit_name": "GPT-5-Codex-Mini", "rate_limit": {
                  "primary_window": { "used_percent": 3, "limit_window_seconds": 604800, "reset_at": 1763075526 } } }
              ]
            }
            """);

        var (plan, windows, values) = CodexProvider.ParseUsage(usage);

        Assert.Equal("pro", plan);
        Assert.Equal(new[] { "5時間枠", "週枠", "GPT-5-Codex-Mini 週枠" }, windows.Select(w => w.Label));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763075526), windows[1].ResetsAt);
        Assert.Equal(new UsageValue("クレジット", "12.5"), Assert.Single(values));
    }

    [Theory]
    [InlineData(18000, "5時間枠")]
    [InlineData(604800, "週枠")]
    [InlineData(86400, "1日枠")]
    [InlineData(7200, "2時間枠")]
    public void Codex_window_labels(double seconds, string expected) =>
        Assert.Equal(expected, CodexProvider.WindowLabel(seconds));

    [Fact]
    public void Gemini_buckets_become_windows_with_used_percent()
    {
        var summary = Parse("""
            {
              "response": { "groups": [
                { "displayName": "Gemini Models", "buckets": [
                  { "window": "weekly", "remainingFraction": 1, "resetTime": "2026-11-16T10:58:25Z" },
                  { "window": "5h", "remainingFraction": 0.75, "resetTime": "2026-11-09T15:58:25Z" },
                  { "window": "weekly" }
                ] }
              ] }
            }
            """);

        var windows = GeminiProvider.ParseSummary(summary);

        Assert.Equal(2, windows.Count);
        Assert.Equal("Gemini Models 週枠", windows[0].Label);
        Assert.Equal(0, windows[0].UsedPercent);
        Assert.Equal(25, windows[1].UsedPercent, precision: 6);
        Assert.Equal(TimeSpan.FromHours(5), windows[1].Length);
    }

    [Fact]
    public void Grok_billing_uses_period_start_and_end_for_length()
    {
        var config = Parse("""
            {
              "currentPeriod": { "type": "USAGE_PERIOD_TYPE_WEEKLY", "start": "2026-11-07T00:00:00Z", "end": "2026-11-14T00:00:00Z" },
              "creditUsagePercent": 23,
              "productUsage": [ { "product": "Grok Build", "usagePercent": 20 } ],
              "prepaidBalance": { "val": "0" }
            }
            """);

        var (windows, values) = GrokProvider.ParseBilling(config);

        var window = Assert.Single(windows);
        Assert.Equal("週枠（全体）", window.Label);
        Assert.Equal(TimeSpan.FromDays(7), window.Length);
        Assert.Equal(new UsageValue("内訳 Grok Build", "うち 20%"), Assert.Single(values));
    }
}
