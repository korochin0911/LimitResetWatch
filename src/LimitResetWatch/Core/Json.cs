using System.Globalization;
using System.Text.Json.Nodes;

namespace LimitResetWatch;

/// <summary>形の決まっていない JSON を、型が違っても例外を出さずに読むための補助。</summary>
internal static class Json
{
    public static JsonNode? Parse(string text) => JsonNode.Parse(text);

    public static JsonNode? Get(JsonNode? node, params string[] path)
    {
        foreach (var key in path)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(key, out node)) return null;
        }
        return node;
    }

    public static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    public static double? Num(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue(out double number)) return number;
        if (value.TryGetValue(out string? text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    public static bool? Bool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out bool flag) ? flag : null;

    /// <summary>ISO 8601 の文字列、または UNIX 時刻（秒かミリ秒）を読む。</summary>
    public static DateTimeOffset? Time(JsonNode? node)
    {
        if (Str(node) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }
        if (Num(node) is not { } number || number <= 0) return null;
        return number > 1e12
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)number)
            : DateTimeOffset.FromUnixTimeSeconds((long)number);
    }

    public static IEnumerable<JsonNode> Items(JsonNode? node) =>
        node is JsonArray array ? array.OfType<JsonNode>() : [];
}
