using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LimitResetWatch;

/// <summary>
/// Claude デスクトップアプリ（Electron）が保存しているログインを読む。読むだけで書き換えない。
/// config.json の oauth:tokenCache は Chromium の safeStorage 形式（"v10" + AES-256-GCM）で暗号化されていて、
/// 鍵は "Local State" の os_crypt.encrypted_key を DPAPI（現在のユーザー）で復号したもの。
/// 読む場所と、使ってよいトークンの条件は Gengchou（MIT, https://github.com/ynjmxn/gengchou）の
/// src/claude_desktop.rs を参考にした。
/// </summary>
internal static class ClaudeDesktopCredentials
{
    const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    const string ApiMarker = ":https://api.anthropic.com:";
    static readonly string[] CacheNames = ["oauth:tokenCache", "oauth:tokenCacheV2"];

    internal sealed record Token(string AccessToken, DateTimeOffset ExpiresAt, string? SubscriptionType, string? RateLimitTier);

    static IEnumerable<string> Roots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[]
        {
            // Microsoft Store（MSIX）版
            Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude"),
            Path.Combine(roaming, "Claude"),
            Path.Combine(local, "Claude"),
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static bool ConfigExists() => Roots().Any(root => File.Exists(Path.Combine(root, "config.json")));

    public static List<Token> ReadTokens(DateTimeOffset now)
    {
        var tokens = new List<Token>();
        foreach (var root in Roots())
        {
            var configPath = Path.Combine(root, "config.json");
            var statePath = Path.Combine(root, "Local State");
            if (!File.Exists(configPath) || !File.Exists(statePath)) continue;

            byte[]? masterKey = null;
            try
            {
                var config = Json.Parse(Paths.ReadShared(configPath));
                var state = Json.Parse(Paths.ReadShared(statePath));
                if (Json.Str(Json.Get(state, "os_crypt", "encrypted_key")) is not { } encryptedKey) continue;
                masterKey = DecryptMasterKey(Convert.FromBase64String(encryptedKey));

                foreach (var name in CacheNames)
                {
                    if (Json.Str(Json.Get(config, name)) is not { } cache) continue;
                    var plain = DecryptV10(Convert.FromBase64String(cache), masterKey);
                    try
                    {
                        tokens.AddRange(Extract(Encoding.UTF8.GetString(plain), now));
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plain);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
                or FormatException or CryptographicException or ArgumentException)
            {
                // 読めない場所は飛ばして、ほかの場所と Claude Code のログインに任せる。
            }
            finally
            {
                if (masterKey is not null) CryptographicOperations.ZeroMemory(masterKey);
            }
        }
        return tokens.DistinctBy(t => t.AccessToken).OrderByDescending(t => t.ExpiresAt).ToList();
    }

    static byte[] DecryptMasterKey(byte[] encrypted)
    {
        var prefix = "DPAPI"u8;
        if (!encrypted.AsSpan().StartsWith(prefix)) throw new CryptographicException("鍵が DPAPI 形式ではない");
        return Native.Unprotect(encrypted[prefix.Length..]);
    }

    internal static byte[] DecryptV10(byte[] encrypted, byte[] key)
    {
        const int NonceLength = 12, TagLength = 16;
        var prefix = "v10"u8;
        if (!encrypted.AsSpan().StartsWith(prefix) || encrypted.Length < prefix.Length + NonceLength + TagLength)
        {
            throw new CryptographicException("v10 形式ではない");
        }
        var payload = encrypted.AsSpan(prefix.Length);
        var nonce = payload[..NonceLength];
        var tag = payload[^TagLength..];
        var cipher = payload[NonceLength..^TagLength];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagLength);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    internal static IEnumerable<Token> Extract(string json, DateTimeOffset now)
    {
        if (Json.Parse(json) is not JsonObject records) yield break;
        foreach (var (key, record) in records)
        {
            if (!IsEligibleKey(key)) continue;
            if (Json.Str(Json.Get(record, "token")) is not { } token
                || !token.StartsWith("sk-ant-oat01-", StringComparison.Ordinal)
                || Json.Time(Json.Get(record, "expiresAt")) is not { } expiresAt
                || expiresAt <= now)
            {
                continue;
            }
            yield return new Token(token, expiresAt,
                Json.Str(Json.Get(record, "subscriptionType")), Json.Str(Json.Get(record, "rateLimitTier")));
        }
    }

    /// <summary>
    /// "{クライアント ID}:{アカウント}:https://api.anthropic.com:{スコープ}" で、user:inference を含むもの。
    /// 新しい版では先頭に "acct:{アカウント ID}|" が付く。
    /// </summary>
    internal static bool IsEligibleKey(string key)
    {
        var bar = key.LastIndexOf('|', Math.Max(0, key.IndexOf(ApiMarker, StringComparison.Ordinal)));
        if (bar >= 0) key = key[(bar + 1)..];
        var prefix = ClientId + ":";
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = key[prefix.Length..];
        var marker = rest.IndexOf(ApiMarker, StringComparison.Ordinal);
        if (marker <= 0) return false;
        return rest[(marker + ApiMarker.Length)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("user:inference");
    }
}
