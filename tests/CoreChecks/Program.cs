using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TextSenderStudio.Core;

int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description); checks++;
}
void Reject(Action action, string description)
{
    try { action(); } catch (ArgumentException) { Check(true, description); return; }
    throw new Exception("FAIL: " + description);
}
async Task RejectAsync<T>(Func<Task> action, string description) where T : Exception
{
    try { await action(); } catch (T) { Check(true, description); return; }
    throw new Exception("FAIL: " + description);
}

var root = Path.Combine(Path.GetTempPath(), "TextSender-Studio-CoreChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new ProjectStore(root);
    Check(!store.LoadSettings().ShowManualControls, "manual controls default hidden");
    Check(store.LoadSettings().LanPort == 19533 && !store.LoadSettings().LanEnabled, "independent LAN settings default off");
    var settings = new AppSettings { EspHost = "192.168.1.88", EspPort = 8080, RobotHost = "192.168.1.89", RobotPort = 8081, RequestTimeoutMs = 3500, ShowManualControls = true };
    store.SaveSettings(settings);
    var reloaded = new ProjectStore(root).LoadSettings();
    Check(reloaded.ShowManualControls && reloaded.EspHost == settings.EspHost && reloaded.EspPort == 8080 && reloaded.RobotHost == settings.RobotHost && reloaded.RobotPort == 8081 && reloaded.RequestTimeoutMs == 3500, "settings survive store recreation");
    var draft = ProjectStore.Clone(settings); draft.EspHost = "discarded.invalid"; draft.ShowManualControls = false;
    Check(settings.EspHost == "192.168.1.88" && store.LoadSettings().ShowManualControls, "discarded settings draft does not alter saved state");
    var invalid = ProjectStore.Clone(settings); invalid.EspPort = 0;
    Reject(() => store.SaveSettings(invalid), "invalid settings cannot replace saved configuration");
    Check(store.LoadSettings().EspPort == 8080 && !File.Exists(Path.Combine(root, "settings.json.tmp")), "settings save leaves valid complete file");
    var one = store.Create("项目一"); var two = store.Create("项目二");
    one.Phrases.Add(new Phrase { Name = "问候", FunctionName = "phrase_hello", Text = "one" });
    one.Shortcuts.Add(new ScriptShortcut { Name = "工具", FunctionName = "start_tools", Path = "scripts/missing.cmd" });
    one.HttpActions.Add(new HttpAction { Name = "状态", FunctionName = "robot_status" });
    store.Save("项目一", one);
    Check(store.ListProjects().Count == 2, "project discovery finds independent projects");
    Check(store.Load("项目二").Phrases.Count == 0 && store.Load("项目二").Shortcuts.Count == 0 && store.Load("项目二").HttpActions.Count == 0, "new project resources remain empty and isolated");
    var changed = store.Load("项目一"); changed.Phrases[0].Text = "updated"; changed.Name = "显示别名"; store.Save("项目一", changed);
    Check(store.Load("项目一").Name == "显示别名" && store.Load("项目一").Phrases[0].Text == "updated" && store.ListProjects().Contains("项目一"), "project alias does not move directory");
    Check(store.LoadSettings().RobotHost == settings.RobotHost, "project edits preserve global device settings");
    Check(store.Resolve("项目一", "labels/sample.png").StartsWith(store.ProjectPath("项目一") + Path.DirectorySeparatorChar), "relative asset resolves inside project");
    Reject(() => store.Resolve("项目一", "../项目二/project.json"), "parent path cannot escape current project");
    Reject(() => store.Resolve("项目一", Path.Combine(root, "outside.txt")), "absolute external asset path rejected");
    foreach (var name in new[] { "..", "../outside", "CON", "LPT1.txt", "trailing.", " space " }) Reject(() => store.ProjectPath(name), "invalid project folder rejected: " + name);
    var duplicate = ProjectStore.Clone(one); duplicate.HttpActions[0].FunctionName = "PHRASE_HELLO";
    Reject(() => store.Save("项目一", duplicate), "cross-category function duplicates are rejected case-insensitively");
    var reserved = ProjectStore.Clone(one); reserved.Phrases[0].FunctionName = "SEND_TEXT";
    Reject(() => store.Save("项目一", reserved), "built-in extension function names cannot be overwritten");
    Check(store.Load("项目一").HttpActions[0].FunctionName == "robot_status", "invalid project save preserves prior resource configuration");
    var fake = new FakeBridge();
    settings.SendDelayMs = 0; settings.PhraseAutoEnter = true; settings.CharacterDelayMs = 17; settings.LineDelayMs = 41; settings.ClearAutoIndent = true;
    var actions = new ProjectActions(settings, one, store.ProjectPath("项目一"), fake, (_, _) => Task.FromResult(0.93), _ => { });
    await actions.CallAsync("PHRASE_HELLO", CancellationToken.None);
    Check(fake.Texts.Single() == ("one\n", 17, 41, true), "phrase function applies auto-enter and timing options");
    Check(actions.FunctionNames.SequenceEqual(["phrase_hello", "start_tools", "robot_status"]), "only current project exposes callable functions");
    await RejectAsync<InvalidOperationException>(() => actions.CallAsync("unconfigured", CancellationToken.None), "unknown function is not executed");
    settings.SendDelayMs = 1000;
    using (var cancellation = new CancellationTokenSource(20))
        await RejectAsync<OperationCanceledException>(() => actions.TypeAsync("cancelled", cancellation.Token), "cancellation stops pending phrase delay");
    Check(fake.Texts.Count == 1, "cancelled phrase never reaches device bridge");
    settings.SendDelayMs = 0;
    await RejectAsync<FileNotFoundException>(() => actions.LaunchAsync(one.Shortcuts[0], CancellationToken.None), "missing shortcut rejected without launching a process");

    await using (var http = new LoopbackHttp([200, 503]))
    {
        settings.RobotHost = "127.0.0.1"; settings.RobotPort = http.Port;
        var post = new HttpAction { Steps = [new() { Method = "POST", Path = "/tap", Body = "{\"strength\":1}", Headers = new() { ["X-Test"] = "configured" }, WaitAfterMs = 0 }] };
        await actions.HttpAsync(post, CancellationToken.None);
        Check(http.Requests[0].StartsWith("POST /tap HTTP/1.1") && http.Requests[0].Contains("X-Test: configured") && http.Requests[0].Contains("{\"strength\":1}"), "HTTP action sends configured method path headers and body");
        await RejectAsync<HttpRequestException>(() => actions.HttpAsync(post, CancellationToken.None), "HTTP failure stops action");
        Check(http.Requests.Count == 2, "HTTP failures are not automatically retried");
    }
    await using (var http = new LoopbackHttp([]))
    {
        settings.RobotPort = http.Port;
        await RejectAsync<ArgumentException>(() => actions.HttpAsync(new HttpAction { Steps = [new() { Path = "//other.invalid/" }] }, CancellationToken.None), "HTTP action cannot replace configured host with URL path");
        Check(http.Requests.Count == 0, "invalid HTTP path makes no request");
        await RejectAsync<ArgumentException>(() => actions.HttpAsync(new HttpAction { Steps = [new() { Path = "/first" }, new() { Path = "/second", WaitAfterMs = -1 }] }, CancellationToken.None), "invalid later HTTP step is validated before first action");
        Check(http.Requests.Count == 0, "invalid sequence makes no partial HTTP action");
    }

    int dispatches = 0, executed = 0;
    using var lan = new LanControlServer(IPAddress.Loopback, 0, (route, request) =>
    {
        dispatches++;
        if (route == "items") return Task.FromResult(new LanControlResponse(200, new { phrases = new[] { "问候" } }));
        if (request.Project != null && request.Project != "项目一") return Task.FromResult(new LanControlResponse(409, new { error = "project" }));
        if (request.Kind != "phrase" || request.Name != "问候") return Task.FromResult(new LanControlResponse(404, new { error = "unknown" }));
        executed++; return Task.FromResult(new LanControlResponse(202, new { ok = true }));
    });
    lan.Start();
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{lan.Port}"), Timeout = TimeSpan.FromSeconds(5) };
    Check((await client.GetAsync("/api/items")).StatusCode == HttpStatusCode.OK, "LAN lists configured actions without authentication");
    async Task<HttpStatusCode> RunJson(string json) => (await client.PostAsync("/api/run", new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode;
    Check(await RunJson("{\"kind\":\"phrase\",\"name\":\"问候\",\"project\":\"项目一\"}") == HttpStatusCode.Accepted && executed == 1, "LAN dispatch accepts configured name and project");
    Check(await RunJson("{\"kind\":\"phrase\",\"name\":\"unknown\"}") == HttpStatusCode.NotFound && executed == 1, "name allowlist rejects unconfigured action");
    Check(await RunJson("{\"kind\":\"phrase\",\"name\":\"问候\",\"project\":\"项目二\"}") == HttpStatusCode.Conflict && executed == 1, "project mismatch does not execute action");
    int beforeInvalid = dispatches;
    foreach (var json in new[] { "{\"kind\":\"phrase\",\"name\":\"问候\",\"text\":\"injected\"}", "{\"kind\":\"script\",\"name\":\"工具\",\"path\":\"any.cmd\"}", "{\"kind\":\"command\",\"name\":\"anything\"}", "{\"kind\":\"phrase\",\"name\":23}", "[]" })
        Check(await RunJson(json) == HttpStatusCode.BadRequest, "LAN parser rejects unsupported data: " + json);
    Check(dispatches == beforeInvalid && executed == 1, "invalid LAN payloads never reach configured action handler");
    lan.Stop(); Check(!lan.IsRunning, "LAN listener stops"); lan.Start(); Check(lan.IsRunning, "LAN listener restarts");
    Console.WriteLine($"Core checks passed: {checks}");
}
finally
{
    // This exact fresh GUID directory was created by this process; never touch user's projects.
    var allowedPrefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "TextSender-Studio-CoreChecks-");
    if (!Path.GetFullPath(root).StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refusing to remove unexpected fixture path.");
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

sealed class FakeBridge : IInputBridge
{
    public List<(string, int, int, bool)> Texts { get; } = [];
    public Task TypeTextAsync(string text, int characterDelayMs, int lineDelayMs, bool clearAutoIndent, CancellationToken token) { token.ThrowIfCancellationRequested(); Texts.Add((text, characterDelayMs, lineDelayMs, clearAutoIndent)); return Task.CompletedTask; }
    public Task<string> StatusAsync(CancellationToken token) => Task.FromResult("fake");
    public Task TapKeyAsync(string keys, int durationMs, CancellationToken token) => Task.CompletedTask;
    public Task PressPadAsync(string buttons, int durationMs, CancellationToken token) => Task.CompletedTask;
    public Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token) => Task.CompletedTask;
    public Task SendKeyboardReportAsync(byte modifiers, byte[] keys, CancellationToken token) => Task.CompletedTask;
    public Task ReleaseAllAsync(CancellationToken token) => Task.CompletedTask;
    public Task ConfigureWifiAsync(string ssid, string password, CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class LoopbackHttp : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    public int Port { get; }
    public List<string> Requests { get; } = [];
    public LoopbackHttp(int[] codes)
    {
        _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _worker = Task.Run(async () =>
        {
            foreach (int code in codes)
            {
                using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = connection.GetStream();
                var header = new List<byte>(); var buffer = new byte[1];
                while (header.Count < 16384)
                {
                    if (await stream.ReadAsync(buffer, _stop.Token) == 0) throw new IOException("incomplete headers");
                    header.Add(buffer[0]); if (header.Count >= 4 && Encoding.ASCII.GetString(header.TakeLast(4).ToArray()) == "\r\n\r\n") break;
                }
                string headers = Encoding.ASCII.GetString(header.ToArray());
                var line = headers.Split("\r\n").FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                int length = line == null ? 0 : int.Parse(line.Split(':')[1]);
                byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, _stop.Token);
                Requests.Add(headers + Encoding.UTF8.GetString(body));
                byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Test\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
                await stream.WriteAsync(response, _stop.Token);
            }
        });
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        try { await _worker; } catch (OperationCanceledException) { } catch (SocketException) when (_stop.IsCancellationRequested) { }
        _stop.Dispose();
    }
}
