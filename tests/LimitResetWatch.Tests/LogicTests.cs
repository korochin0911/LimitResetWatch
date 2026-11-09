using System.Security.Cryptography;
using System.Text;

namespace LimitResetWatch.Tests;

public class LogicTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(30, "あと30分")]
    [InlineData(192, "あと3時間12分")]
    [InlineData(47 * 60 + 59, "あと47時間59分")]
    [InlineData(4 * 1440 + 180, "あと4日3時間")]
    [InlineData(0, "リセット済み")]
    public void Until_text(int minutes, string expected) =>
        Assert.Equal(expected, TimeText.Until(Now.AddMinutes(minutes), Now));

    [Theory]
    [InlineData(45, "45", "m")]
    [InlineData(59, "59", "m")]
    [InlineData(60, "1", "h")]
    [InlineData(18 * 60 + 40, "18", "h")]
    [InlineData(47 * 60 + 59, "47", "h")]
    [InlineData(48 * 60, "2", "d")]
    [InlineData(-5, "0", "m")]
    public void Compact_time(int minutes, string number, string unit) =>
        Assert.Equal((number, unit), TimeText.Compact(Now.AddMinutes(minutes), Now));

    [Fact]
    public void Primary_is_the_longest_window_that_resets_first()
    {
        var session = new UsageWindow("5時間枠", 64, Now.AddHours(3), TimeSpan.FromHours(5));
        var weeklyLate = new UsageWindow("週枠A", 0, Now.AddDays(6), TimeSpan.FromDays(7));
        var weeklySoon = new UsageWindow("週枠B", 22, Now.AddDays(2), TimeSpan.FromDays(7));

        Assert.Same(weeklySoon, UsageMath.PickPrimary([session, weeklyLate, weeklySoon], Now));
    }

    [Fact]
    public void Primary_skips_windows_that_already_reset()
    {
        var expiredWeek = new UsageWindow("週枠", 90, Now.AddMinutes(-1), TimeSpan.FromDays(7));
        var session = new UsageWindow("5時間枠", 10, Now.AddHours(1), TimeSpan.FromHours(5));

        Assert.Same(session, UsageMath.PickPrimary([expiredWeek, session], Now));
    }

    [Fact]
    public void Elapsed_is_the_share_of_the_window_that_has_passed()
    {
        var window = new UsageWindow("週枠", 0, Now.AddDays(5.25), TimeSpan.FromDays(7));
        Assert.Equal(0.25, UsageMath.Elapsed(window, Now)!.Value, precision: 6);
        Assert.Null(UsageMath.Elapsed(window with { Length = null }, Now));
    }

    [Fact]
    public void Merge_keeps_previous_values_as_stale_when_fetch_fails()
    {
        var previous = new ProviderSnapshot
        {
            Id = ProviderId.Grok,
            IsConfigured = true,
            Windows = [new UsageWindow("週枠（全体）", 23, Now.AddDays(5), TimeSpan.FromDays(7))],
            FetchedAt = Now.AddHours(-2),
        };
        var failed = new ProviderSnapshot { Id = ProviderId.Grok, IsConfigured = true, Error = "ログイン切れ" };

        var merged = UsageMath.Merge(previous, failed);

        Assert.True(merged.IsStale);
        Assert.Equal("ログイン切れ", merged.Error);
        Assert.Equal(previous.Windows, merged.Windows);
        Assert.Equal(previous.FetchedAt, merged.FetchedAt);

        var fresh = failed with { Error = null, Windows = previous.Windows };
        Assert.False(UsageMath.Merge(merged, fresh).IsStale);
    }

    [Fact]
    public void Tooltip_fits_notify_icon_limit()
    {
        var windows = Enumerable.Range(0, 10)
            .Select(i => new UsageWindow($"Claude and GPT models 週枠 {i}", i, Now.AddDays(1 + i), TimeSpan.FromDays(7)))
            .ToList();
        var snapshot = new ProviderSnapshot { Id = ProviderId.Gemini, IsConfigured = true, Windows = windows };

        var (model, tooltip) = TrayPresenter.Build(snapshot, Now, lightTaskbar: false);

        Assert.True(tooltip.Length <= 127);
        Assert.Equal(("24", "h"), (model.Number, model.Unit));
    }

    [Theory]
    [InlineData("9d1c250a-e61b-44d9-88ed-5944d1962f5e:acct:https://api.anthropic.com:user:profile user:inference", true)]
    [InlineData("9d1c250a-e61b-44d9-88ed-5944d1962f5e:acct:https://api.anthropic.com:user:profile", false)]
    [InlineData("other-client:acct:https://api.anthropic.com:user:inference", false)]
    [InlineData("9d1c250a-e61b-44d9-88ed-5944d1962f5e::https://api.anthropic.com:user:inference", false)]
    [InlineData("acct:1234|9d1c250a-e61b-44d9-88ed-5944d1962f5e:acct:https://api.anthropic.com:user:inference user:profile", true)]
    [InlineData("acct:1234|89355bc3-cbfd-4382-905b-976645cad410:acct:https://api.anthropic.com:user:inference", false)]
    public void Desktop_cache_keys(string key, bool eligible) =>
        Assert.Equal(eligible, ClaudeDesktopCredentials.IsEligibleKey(key));

    [Fact]
    public void Desktop_cache_is_decrypted_and_filtered()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var expires = Now.AddHours(1).ToUnixTimeMilliseconds();
        var plain = Encoding.UTF8.GetBytes($$"""
            {
              "9d1c250a-e61b-44d9-88ed-5944d1962f5e:acct:https://api.anthropic.com:user:inference": { "token": "sk-ant-oat01-good", "expiresAt": {{expires}} },
              "9d1c250a-e61b-44d9-88ed-5944d1962f5e:acct:https://api.anthropic.com:user:inference user:x": { "token": "sk-ant-oat01-old", "expiresAt": 1 },
              "9d1c250a-e61b-44d9-88ed-5944d1962f5e:acct:https://api.anthropic.com:user:profile": { "token": "sk-ant-oat01-scope", "expiresAt": {{expires}} }
            }
            """);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        byte[] encrypted = [.. "v10"u8, .. nonce, .. cipher, .. tag];

        var decrypted = ClaudeDesktopCredentials.DecryptV10(encrypted, key);
        var tokens = ClaudeDesktopCredentials.Extract(Encoding.UTF8.GetString(decrypted), Now).ToList();

        Assert.Equal("sk-ant-oat01-good", Assert.Single(tokens).AccessToken);
    }
}
