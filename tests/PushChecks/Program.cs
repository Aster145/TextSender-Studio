using System.Net;
using System.Text.Json;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

var checks = 0;
void Check(bool value, string description) { if (!value) throw new Exception("FAIL " + description); checks++; Console.WriteLine("PASS " + description); }
void Reject(Action action, string description)
{
    try { action(); } catch (ArgumentException) { Check(true, description); return; }
    throw new Exception("FAIL " + description);
}
var defaults = new PushNotificationSettings();
Check(defaults.Channels.Select(c => c.Name).SequenceEqual(["PushPlus", "Bark", "自定义 Webhook"]) && defaults.Channels.All(c => !c.Enabled), "original EasyCon service presets default disabled");
using var handler = new RecordingHandler();
using var dispatcher = new PushNotificationDispatcher(handler);
Check((await dispatcher.SendAsync(defaults, "ignored")).Count == 0 && handler.Requests.Count == 0, "disabled notification services make no requests");
var bark = defaults.Channels[1]; bark.Enabled = true; bark.Token = "token/with?chars";
bark.Variables["group"] = "中文 & group";
var content = "中文 / & ? # \"quoted\"\nnext line";
var results = await dispatcher.SendAsync(defaults, content, "标题 & title");
var request = handler.Requests.Single();
Check(request.Url.Contains(Uri.EscapeDataString(content)) && request.Url.Contains(Uri.EscapeDataString(bark.Token)) && request.Url.Contains("group=" + Uri.EscapeDataString(bark.Variables["group"])), "URL content, token and variables are escaped independently");
Check(results.Single().Contains("成功") && !results.Single().Contains(bark.Token), "successful result excludes credentials");

var custom = PushNotificationTemplates.CreateDefaults()[2]; custom.Enabled = true; custom.Token = "private-secret";
custom.Body = "{\"title\":\"{{title}}\",\"msg\":\"{{content}}\"}";
var customSettings = new PushNotificationSettings { Channels = [custom] };
await dispatcher.SendAsync(customSettings, content, "标题");
var posted = handler.Requests.Last(); using var json = JsonDocument.Parse(posted.Body);
Check(json.RootElement.GetProperty("msg").GetString() == content && json.RootElement.GetProperty("title").GetString() == "标题", "JSON substitution preserves Unicode, quotes and newlines");
Check(posted.Method == "POST" && posted.Authorization == "Bearer private-secret" && posted.ContentType?.StartsWith("application/json") == true, "custom POST expands authorization and content type");

custom.Headers["Content-Type"] = "application/x-www-form-urlencoded"; custom.Body = "message={{content}}";
await dispatcher.SendAsync(customSettings, content);
Check(handler.Requests.Last().Body == "message=" + Uri.EscapeDataString(content), "form POST encodes substituted fields");

var invalid = ProjectStore.Clone(customSettings); invalid.Channels[0].Method = "DELETE";
Reject(() => PushNotificationDispatcher.Validate(invalid), "unsupported notification method rejected");
invalid = ProjectStore.Clone(customSettings); invalid.Channels[0].Token = "";
Reject(() => PushNotificationDispatcher.Validate(invalid), "enabled credential placeholder requires token");
invalid = ProjectStore.Clone(customSettings); invalid.Channels[0].Url = "https://example.com/{{missing}}";
Reject(() => PushNotificationDispatcher.Validate(invalid), "unknown template variable rejected");
invalid = ProjectStore.Clone(customSettings); invalid.Channels[0].Headers["X-Test"] = "line\r\ninjection";
Reject(() => PushNotificationDispatcher.Validate(invalid), "invalid header newline rejected before dispatch");
invalid = ProjectStore.Clone(customSettings); invalid.TimeoutMs = 0;
Reject(() => PushNotificationDispatcher.Validate(invalid), "notification timeout uses positive milliseconds");
var before = handler.Requests.Count;
invalid = ProjectStore.Clone(customSettings); invalid.Channels.Add(new() { Name = "bad", Enabled = true, Url = "not-a-url" });
try { await dispatcher.SendAsync(invalid, "content"); throw new Exception("invalid batch sent"); }
catch (ArgumentException) { Check(handler.Requests.Count == before, "entire batch validated before any request"); }

using (var failures = new PushNotificationDispatcher(new RecordingHandler { Status = HttpStatusCode.Unauthorized }))
{
    var failure = (await failures.SendAsync(customSettings, "message")).Single();
    Check(failure.Contains("401") && !failure.Contains("private-secret"), "failure exposes HTTP status without server response secrets");
}
using (var failures = new PushNotificationDispatcher(new RecordingHandler { ThrowSecret = true }))
{
    var failure = (await failures.SendAsync(customSettings, "message")).Single();
    Check(failure.Contains("网络") && !failure.Contains("private-secret"), "network exception details do not leak credentials");
}
using (var slow = new PushNotificationDispatcher(new RecordingHandler { Delay = true }))
{
    var slowSettings = ProjectStore.Clone(customSettings); slowSettings.TimeoutMs = 30;
    Check((await slow.SendAsync(slowSettings, "message")).Single().Contains("超时"), "request timeout completes with useful result");
    using var cancellation = new CancellationTokenSource(30);
    slowSettings.TimeoutMs = 10000;
    try { await slow.SendAsync(slowSettings, "message", token: cancellation.Token); throw new Exception("cancellation ignored"); }
    catch (OperationCanceledException) { Check(true, "script cancellation interrupts in-flight notification"); }
}
var actions = new NotificationActions();
await new StudioScriptEngine().RunAsync("ALERT \"成功提醒\"", actions, CancellationToken.None);
Check(actions.Notifications.SequenceEqual(["成功提醒"]) && actions.Messages.Count == 0, "EasyCon ALERT invokes configured notification adapter");
Console.WriteLine($"Push checks passed: {checks}");

sealed class RecordingHandler : HttpMessageHandler
{
    public List<(string Url, string Method, string Body, string? ContentType, string? Authorization)> Requests { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public bool ThrowSecret { get; set; }
    public bool Delay { get; set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Delay) await Task.Delay(10000, cancellationToken);
        if (ThrowSecret) throw new HttpRequestException("private-secret from request URL");
        Requests.Add((request.RequestUri!.AbsoluteUri, request.Method.Method,
            request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Content?.Headers.ContentType?.ToString(), request.Headers.Authorization?.ToString()));
        return new HttpResponseMessage(Status) { Content = new StringContent("server echoes private-secret") };
    }
}

sealed class NotificationActions : IScriptActions, IScriptNotificationActions
{
    public List<string> Notifications { get; } = [];
    public List<string> Messages { get; } = [];
    public IReadOnlyList<string> FunctionNames => [];
    public void Log(string message) => Messages.Add(message);
    public Task AlertAsync(string content, CancellationToken token) { token.ThrowIfCancellationRequested(); Notifications.Add(content); return Task.CompletedTask; }
    public Task PadAsync(string buttons, int durationMs, CancellationToken token) => Task.CompletedTask;
    public Task KeyAsync(string keys, int durationMs, CancellationToken token) => Task.CompletedTask;
    public Task TypeAsync(string text, CancellationToken token) => Task.CompletedTask;
    public Task<double> MatchAsync(string labelName, CancellationToken token) => Task.FromResult(0.0);
    public Task CallAsync(string functionName, CancellationToken token) => Task.CompletedTask;
}
