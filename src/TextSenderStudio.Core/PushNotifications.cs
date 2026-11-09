using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TextSenderStudio.Core;

public sealed class PushNotificationSettings
{
    public int TimeoutMs { get; set; } = 10000;
    public List<PushNotificationChannel> Channels { get; set; } = PushNotificationTemplates.CreateDefaults();
}

public sealed class PushNotificationChannel
{
    public string Name { get; set; } = "自定义 Webhook";
    public bool Enabled { get; set; }
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public string Token { get; set; } = "";
    public string Body { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = [];
    public Dictionary<string, string> Variables { get; set; } = [];
}

public static class PushNotificationTemplates
{
    // Same three service templates as EasyCon's GenerateDefaultAlert; credentials
    // stay empty and every service is opt-in in the local settings file.
    public static List<PushNotificationChannel> CreateDefaults() =>
    [
        new() { Name = "PushPlus", Url = "https://www.pushplus.plus/send/{{token}}?content={{content}}&title={{title}}" },
        new()
        {
            Name = "Bark", Url = "https://api.day.app/{{token}}/{{title}}/{{content}}?group={{group}}&icon={{icon}}",
            Variables = new() { ["group"] = "TextSender Studio", ["icon"] = "" }
        },
        new()
        {
            Name = "自定义 Webhook", Method = "POST", Url = "https://example.com/webhook",
            Headers = new() { ["Authorization"] = "Bearer {{token}}", ["Content-Type"] = "application/json" },
            Body = "{\"msg\":\"{{content}}\"}", Variables = new() { ["chat_id"] = "" }
        }
    ];
}

public sealed class PushNotificationDispatcher : IDisposable
{
    private static readonly Regex Placeholder = new(@"\{\{([A-Za-z_][A-Za-z0-9_]*)\}\}", RegexOptions.CultureInvariant);
    private readonly HttpClient _client;

    public PushNotificationDispatcher(HttpMessageHandler? handler = null)
    {
        _client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static void Validate(PushNotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.TimeoutMs is < 1 or > 600000) throw new ArgumentException("推送超时须为 1–600000 ms。");
        foreach (var channel in settings.Channels.Where(c => c.Enabled))
        {
            if (string.IsNullOrWhiteSpace(channel.Name)) throw new ArgumentException("推送服务名称不能为空。");
            if (string.IsNullOrWhiteSpace(channel.Token) &&
                new[] { channel.Url, channel.Body }.Concat(channel.Headers.Values).Any(t => t.Contains("{{token}}")))
                throw new ArgumentException(channel.Name + "：请填写 Token 或移除不需要的 Token 占位符。");
            try { using var request = BuildRequest(channel, "消息", "TextSender Studio"); }
            catch (ArgumentException) { throw; }
            catch { throw new ArgumentException(channel.Name + "：请求模板无效，请检查地址、请求头和正文。"); }
        }
    }

    public async Task<IReadOnlyList<string>> SendAsync(PushNotificationSettings settings, string content,
        string title = "TextSender Studio", CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Validate(settings);
        var active = settings.Channels.Where(c => c.Enabled).Select(ProjectStore.Clone).ToArray();
        if (active.Length == 0) return [];
        // Build the whole batch before any side effect so a malformed template
        // never produces a partially dispatched notification.
        var requests = new List<HttpRequestMessage>();
        try
        {
            foreach (var channel in active) requests.Add(BuildRequest(channel, content, title));
            return await Task.WhenAll(active.Select((channel, index) => SendOneAsync(channel.Name,
                requests[index], settings.TimeoutMs, token))).ConfigureAwait(false);
        }
        finally { foreach (var request in requests) request.Dispose(); }
    }

    private async Task<string> SendOneAsync(string name, HttpRequestMessage request, int timeoutMs, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(timeoutMs);
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? $"[{name}] 推送成功" : $"[{name}] 推送失败：HTTP {(int)response.StatusCode}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return $"[{name}] 推送超时"; }
        // Exception messages and server response bodies can echo URLs/tokens.
        // Keep credentials out of execution logs and user-facing errors.
        catch { return $"[{name}] 推送失败：网络请求未完成"; }
    }

    private static HttpRequestMessage BuildRequest(PushNotificationChannel channel, string content, string title)
    {
        if (channel.Method.ToUpperInvariant() is not ("GET" or "POST"))
            throw new ArgumentException(channel.Name + "：推送方法仅支持 GET 或 POST。");
        var values = new Dictionary<string, string>(channel.Variables, StringComparer.Ordinal)
            { ["token"] = channel.Token, ["content"] = content, ["title"] = title };
        string Expand(string template, Func<string, string> encode) => Placeholder.Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? encode(value ?? "")
                : throw new ArgumentException(channel.Name + "：模板变量未配置：" + match.Groups[1].Value));
        var url = Expand(channel.Url, Uri.EscapeDataString);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            throw new ArgumentException(channel.Name + "：请填写完整的 HTTP / HTTPS 推送地址。");
        var request = new HttpRequestMessage(new HttpMethod(channel.Method.ToUpperInvariant()), uri);
        try
        {
            var typeTemplate = channel.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
            var contentType = string.IsNullOrWhiteSpace(typeTemplate) ? "application/json" : Expand(typeTemplate, value => value);
            if (!MediaTypeHeaderValue.TryParse(contentType, out var parsedType))
                throw new ArgumentException(channel.Name + "：Content-Type 无效。");
            if (channel.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                var isJson = parsedType.MediaType?.Equals("application/json", StringComparison.OrdinalIgnoreCase) == true
                    || parsedType.MediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;
                var body = Expand(channel.Body, value => isJson ? JsonSerializer.Serialize(value)[1..^1]
                    : parsedType.MediaType?.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true
                        ? Uri.EscapeDataString(value) : value);
                if (isJson && body.Length > 0)
                {
                    try { using var json = JsonDocument.Parse(body); }
                    catch (JsonException) { throw new ArgumentException(channel.Name + "：JSON 正文无效，请将占位符放在字符串引号内。"); }
                }
                request.Content = new StringContent(body, Encoding.UTF8);
                request.Content.Headers.ContentType = parsedType;
            }
            foreach (var header in channel.Headers.Where(h => !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            {
                var value = Expand(header.Value, text => text);
                if (value.Contains('\r') || value.Contains('\n') || !request.Headers.TryAddWithoutValidation(header.Key, value))
                    throw new ArgumentException(channel.Name + "：请求头无效。");
            }
            return request;
        }
        catch { request.Dispose(); throw; }
    }

    public void Dispose() => _client.Dispose();
}
