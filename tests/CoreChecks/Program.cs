using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

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

    var soundProject = store.Load("项目一");
    soundProject.Sounds.Add(new() { Name = "完成提示", FunctionName = "sound_notice", Source = "System", SystemSound = "Exclamation" });
    soundProject.Sounds.Add(new() { Name = "自选音乐", FunctionName = "sound_music", Source = "File", FilePath = "sounds/通知音乐.mp3" });
    store.Save("项目一", soundProject);
    var restoredSounds = store.Load("项目一").Sounds;
    Check(restoredSounds.Count == 2 && restoredSounds[0].SystemSound == "Exclamation" && restoredSounds[1].FilePath == "sounds/通知音乐.mp3", "system and file sound functions persist with project");
    Check(store.Load("项目二").Sounds.Count == 0, "sound resources remain isolated per project");
    Check(JsonSerializer.Deserialize<StudioProject>("{\"Name\":\"existing\"}")!.Sounds.Count == 0, "existing project without sounds opens with empty sound collection");
    foreach (var collision in new[] { "PHRASE_HELLO", "START_TOOLS", "ROBOT_STATUS" })
    {
        var soundConflict = ProjectStore.Clone(soundProject); soundConflict.Sounds[0].FunctionName = collision;
        Reject(() => store.Save("项目一", soundConflict), "sound function cannot shadow another resource: " + collision);
    }
    var invalidSound = ProjectStore.Clone(soundProject); invalidSound.Sounds[0].Source = "Unknown";
    Reject(() => store.Save("项目一", invalidSound), "unknown sound source rejected");
    invalidSound = ProjectStore.Clone(soundProject); invalidSound.Sounds[0].SystemSound = "Unknown";
    Reject(() => store.Save("项目一", invalidSound), "unknown system sound preset rejected");
    invalidSound = ProjectStore.Clone(soundProject); invalidSound.Sounds[1].FilePath = "";
    Reject(() => store.Save("项目一", invalidSound), "file sound requires selected path");
    Check(store.Load("项目一").Sounds[0].FunctionName == "sound_notice" && store.Load("项目一").Sounds[1].FilePath.EndsWith(".mp3"), "rejected sound edits preserve saved resources");

    var soundEvents = new List<string>();
    var playbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var playbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    SoundAction? playedSound = null;
    var soundActions = new ProjectActions(settings, soundProject, store.ProjectPath("项目一"), fake,
        (_, _) => Task.FromResult(0.0), soundEvents.Add, async (sound, token) =>
        {
            playedSound = sound; playbackStarted.TrySetResult();
            await playbackFinished.Task.WaitAsync(token);
        });
    var scriptWithSound = new StudioScriptEngine().RunAsync("CALL sound_music\nPRINT after-sound", soundActions, CancellationToken.None);
    await playbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!scriptWithSound.IsCompleted && soundEvents.Count == 0 && playedSound?.FilePath == "sounds/通知音乐.mp3", "script CALL routes file sound and waits for playback completion");
    playbackFinished.TrySetResult(); await scriptWithSound;
    Check(soundEvents.Count == 2 && soundEvents[0] == "声音提醒：自选音乐" && soundEvents[1].Contains("after-sound"), "script continues only after sound callback completes");
    await soundActions.CallAsync("SOUND_NOTICE", CancellationToken.None);
    Check(playedSound?.SystemSound == "Exclamation" && playedSound.Source == "System", "sound function dispatch is case-insensitive and preserves preset");
    var isolatedActions = new ProjectActions(settings, two, store.ProjectPath("项目二"), fake, (_, _) => Task.FromResult(0.0), _ => { });
    Check(!isolatedActions.FunctionNames.Contains("sound_music") && soundActions.FunctionNames.Contains("sound_music"), "sound completion function list follows current project");
    await RejectAsync<InvalidOperationException>(() => isolatedActions.CallAsync("sound_music", CancellationToken.None), "another project's sound cannot be invoked");
    var noAudioHost = new ProjectActions(settings, soundProject, store.ProjectPath("项目一"), fake, (_, _) => Task.FromResult(0.0), _ => { });
    await RejectAsync<InvalidOperationException>(() => noAudioHost.CallAsync("sound_notice", CancellationToken.None), "missing sound adapter reports unsupported host");

    using (var cancellation = new CancellationTokenSource())
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interrupted = false;
        var cancelledEvents = new List<string>();
        var cancellableSound = new ProjectActions(settings, soundProject, store.ProjectPath("项目一"), fake,
            (_, _) => Task.FromResult(0.0), cancelledEvents.Add, async (_, token) =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { interrupted = true; throw; }
            });
        var script = new StudioScriptEngine().RunAsync("CALL sound_music\nPRINT should-not-run", cancellableSound, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await RejectAsync<OperationCanceledException>(() => script, "script cancellation interrupts fake sound playback");
        Check(interrupted && cancelledEvents.Count == 0, "cancelled sound never logs completion or runs next instruction");
    }
    Check(fake.Texts.Count == 1, "sound calls never send keyboard text to input bridge");

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
        await RejectAsync<ArgumentException>(() => actions.HttpAsync(new HttpAction { Steps = [new() { Path = "//other.invalid/" }] }, CancellationToken.None), "HTTP rejects ambiguous protocol-relative addresses");
        Check(http.Requests.Count == 0, "invalid HTTP path makes no request");
        await RejectAsync<ArgumentException>(() => actions.HttpAsync(new HttpAction { Steps = [new() { Path = "/first" }, new() { Path = "/second", WaitAfterMs = -1 }] }, CancellationToken.None), "invalid later HTTP step is validated before first action");
        Check(http.Requests.Count == 0, "invalid sequence makes no partial HTTP action");
    }

    await using (var firstDevice = new LoopbackHttp([200, 200, 200, 200]))
    await using (var secondDevice = new LoopbackHttp([200, 200, 200, 200]))
    {
        settings.RobotHost = "";
        var firstUrl = $"http://127.0.0.1:{firstDevice.Port}";
        var secondUrl = $"http://127.0.0.1:{secondDevice.Port}";
        await actions.HttpAsync(new() { Steps = [new() { Path = firstUrl + "/first" }, new() { Path = secondUrl + "/second" }] }, CancellationToken.None);
        Check(firstDevice.Requests.Single().StartsWith("GET /first ") && secondDevice.Requests.Single().StartsWith("GET /second "), "one HTTP action addresses two devices without any saved default");
        settings.RobotHost = "127.0.0.1"; settings.RobotPort = firstDevice.Port;
        await actions.HttpAsync(new() { Steps = [new() { Path = secondUrl + "/override" }] }, CancellationToken.None);
        Check(secondDevice.Requests.Last().StartsWith("GET /override ") && firstDevice.Requests.Count == 1, "full step URL overrides legacy global default");
        await actions.HttpAsync(new() { Steps = [new() { Path = "/legacy" }] }, CancellationToken.None);
        Check(firstDevice.Requests.Last().StartsWith("GET /legacy "), "existing relative-path actions retain configured default device");

        settings.RobotHost = "";
        var directScript = $"HTTP_GET \"http://localhost:{firstDevice.Port}/script-get\"\nHTTP_POST \"{secondUrl}/script-post\", \"{{\\\"message\\\":\\\"ok\\\"}}\"\nHTTP_REQUEST \"PATCH\", \"{firstUrl}/script-patch\", \"payload\", \"text/plain; charset=utf-8\"\nHTTP_POST \"{secondUrl}/empty-post\", \"\"";
        await new StudioScriptEngine().RunAsync(directScript, actions, CancellationToken.None);
        Check(firstDevice.Requests[2].StartsWith("GET /script-get ") && firstDevice.Requests[2].Contains($"Host: localhost:{firstDevice.Port}"), "direct HTTP_GET script accepts hostname and custom port without defaults");
        Check(secondDevice.Requests[2].StartsWith("POST /script-post ") && secondDevice.Requests[2].Contains("{\"message\":\"ok\"}") && secondDevice.Requests[2].Contains("application/json"), "direct HTTP_POST script sends JSON body to another device");
        Check(firstDevice.Requests[3].StartsWith("PATCH /script-patch ") && firstDevice.Requests[3].Contains("text/plain; charset=utf-8") && firstDevice.Requests[3].EndsWith("payload"), "HTTP_REQUEST script accepts parameterized content type with selected method and body");
        Check(secondDevice.Requests[3].StartsWith("POST /empty-post ") && secondDevice.Requests[3].Contains("Content-Length: 0") && secondDevice.Requests[3].Contains("application/json"), "HTTP_POST with empty body retains JSON content type and zero content length");
    }
    var secureTarget = HttpRequestAddress.Resolve(new() { Path = "https://device.example:8443/api/state?value=1" }, settings);
    Check(secureTarget.Scheme == "https" && secureTarget.Host == "device.example" && secureTarget.Port == 8443, "HTTPS hostname URL is accepted independent of default host");
    foreach (var builtIn in new[] { "HTTP_GET", "HTTP_POST", "HTTP_REQUEST" })
    {
        var conflict = ProjectStore.Clone(one); conflict.Phrases[0].FunctionName = builtIn.ToLowerInvariant();
        Reject(() => ProjectStore.ValidateFunctions(conflict), "direct HTTP builtin name is reserved: " + builtIn);
    }
    await using (var untouched = new LoopbackHttp([]))
    {
        foreach (var invalidTarget in new[] { "//other.invalid/path", "ftp://example.com/path", "file:///C:/test", "http:///missing", "http://", "http://example.com:99999/", "/space here", "/path#fragment", " " })
            await RejectAsync<ArgumentException>(() => actions.HttpAsync(new() { Steps = [new() { Path = $"http://127.0.0.1:{untouched.Port}/valid-first" }, new() { Path = invalidTarget }] }, CancellationToken.None), "malformed later address blocks entire request sequence: " + invalidTarget);
        Check(untouched.Requests.Count == 0, "malformed full URL never permits first step to execute");
        await RejectAsync<ArgumentException>(() => actions.HttpAsync(new() { Steps = [new() { Path = "/missing-default" }] }, CancellationToken.None), "relative path without old default reports missing address");
        await RejectAsync<ArgumentException>(() => actions.RequestHttpAsync("GET", "/relative", "", "application/json", CancellationToken.None), "direct script HTTP API requires complete URL");
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
