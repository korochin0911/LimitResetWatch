using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LimitResetWatch;

internal sealed class HttpStatusException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

internal static class Http
{
    static readonly HttpClient Client = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LimitResetWatch/0.1");
        return client;
    }

    public static Task<JsonNode> GetJsonAsync(string url, CancellationToken ct, params (string Name, string Value)[] headers) =>
        SendAsync(HttpMethod.Get, url, null, headers, ct);

    public static Task<JsonNode> PostJsonAsync(string url, string body, CancellationToken ct, params (string Name, string Value)[] headers) =>
        SendAsync(HttpMethod.Post, url, body, headers, ct);

    static async Task<JsonNode> SendAsync(
        HttpMethod method, string url, string? body, (string Name, string Value)[] headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        try { json = JsonNode.Parse(text); } catch (JsonException) { }

        if (!response.IsSuccessStatusCode)
        {
            var message = Json.Str(Json.Get(json, "error", "message"))
                ?? Json.Str(Json.Get(json, "detail"))
                ?? (text.Length > 0 ? text[..Math.Min(200, text.Length)] : response.ReasonPhrase);
            throw new HttpStatusException((int)response.StatusCode, $"HTTP {(int)response.StatusCode}: {message}");
        }
        return json ?? throw new InvalidOperationException("応答が JSON ではない");
    }
}
