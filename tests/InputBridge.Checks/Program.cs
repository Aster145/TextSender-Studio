using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using TextSenderStudio.Core;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Throws<T>(Func<Task> action, string message) where T : Exception
{ try { await action(); } catch (T) { checks++; return; } throw new Exception(message); }
Check(InputBridge.ParsePad("A+R+UP+LEFT") == (36, 7), "combined pad mapping");
Check(InputBridge.ParsePad("UP+DOWN+LEFT+RIGHT") == (0, 15), "opposite dpad neutral");
Check(InputBridge.ParsePad("LCLICK+RCLICK") == InputBridge.ParsePad("L3+R3"), "EasyCon stick click aliases match bridge button masks");
foreach (var (alias, expanded) in new[] { ("UPLEFT", "UP+LEFT"), ("UPRIGHT", "UP+RIGHT"), ("DOWNLEFT", "DOWN+LEFT"), ("DOWNRIGHT", "DOWN+RIGHT") })
    Check(InputBridge.ParsePad(alias) == InputBridge.ParsePad(expanded), "EasyCon diagonal alias: " + alias);
var parsed = InputBridge.ParseKeyboard("CTRL+SHIFT+A+B");
Check(parsed.Modifiers == 3 && parsed.Keys.SequenceEqual(new byte[] { 4, 5 }), "keyboard modifier and six-key report mapping");
await Throws<ArgumentException>(() => Task.FromResult(InputBridge.ParsePad("UNKNOWN")), "unknown pad name");

var fake = new DeviceHandler();
using var http = new HttpClient(fake);
await using (var unused = new InputBridge(new AppSettings(), http)) { }
Check(fake.Calls.IsEmpty, "unused bridge disposal never contacts device");
await using (var readOnly = new InputBridge(new AppSettings(), http)) { await readOnly.StatusAsync(default); }
Check(fake.Calls.All(c => c.Path == "/status"), "status-only disposal never posts cancel");
await using (var bridge = new InputBridge(new AppSettings(), http))
{
    await bridge.TypeTextAsync("Ab\nC", 0, 0, false, default);
    var text = fake.Calls.Last(x => x.Path == "/text");
    Check(text.Values["gap"] == "0" && text.Values["line"] == "0", "zero timing forwarded");
    Check(text.Values["plain"] == "Ab\nC" && text.Job.Length == 32, "text and unique job");
    await Throws<ArgumentException>(() => bridge.TypeTextAsync("中文", 30, 0, false, default), "ASCII rejection");
    fake.Caps = true;
    await bridge.TypeTextAsync("A", 1, 0, false, default);
    Check(fake.Calls.Any(x => x.Path == "/tap" && x.Values["key"] == "57"), "disable caps before text");
    fake.Caps = false;
    await bridge.TypeTextAsync("x\ny", 0, 0, true, default);
    Check(fake.Calls.Any(x => x.Path == "/tap" && x.Values["key"] == "74" && x.Values["mod"] == "2"), "clear indent ShiftHome");
    Check(fake.Calls.Any(x => x.Path == "/tap" && x.Values["key"] == "42"), "clear indent Backspace");
    await bridge.SetPadStateAsync("A+R", 0, 255, 128, 128, default);
    await Task.Delay(360);
    Check(fake.Calls.Any(x => x.Path == "/pad/stream" && x.Values["frames"] == "36,15,0,255,128,128,16"), "stick and buttons forwarded");
    Check(fake.Calls.Any(x => x.Path == "/pad/stream" && x.Values["frames"] == ""), "pad lease renewal");
    await bridge.ReleaseAllAsync(default);
    await bridge.SendKeyboardReportAsync(3, [4, 5, 6, 7, 8, 9], default);
    await Task.Delay(360);
    var reports = fake.Calls.Where(x => x.Path == "/keyboard/report").ToList();
    Check(reports.Count >= 2 && reports[0].Values["k5"] == "9", "six simultaneous keys and heartbeat");
    Check(reports[0].Values["session"] == reports[1].Values["session"] && reports[1].Values["sequence"] == "1", "ordered keyboard sequence");
    await bridge.SendKeyboardReportAsync(0, [], default);
    int before = fake.Calls.Count(x => x.Path == "/keyboard/report");
    await Task.Delay(360);
    Check(fake.Calls.Count(x => x.Path == "/keyboard/report") == before, "neutral stops lease renewal");
    await bridge.ConfigureWifiAsync("ExampleWiFi", "example-pass", default);
    Check(fake.Calls.Last().Path == "/wifi" && fake.Calls.Last().Values["ssid"] == "ExampleWiFi", "WiFi form");
    fake.FailPath = "/text";
    int priorText = fake.Calls.Count(x => x.Path == "/text");
    await Throws<HttpRequestException>(() => bridge.TypeTextAsync("x", 0, 0, false, default), "execution error surfaced");
    Check(fake.Calls.Count(x => x.Path == "/text") == priorText + 1, "uncertain action never retried");
    fake.FailPath = null;
    fake.BusyForever = true;
    using var cancel = new CancellationTokenSource(100);
    await Throws<OperationCanceledException>(() => bridge.TypeTextAsync("x", 30, 0, false, cancel.Token), "job cancellation");
    Check(fake.Calls.Last().Path == "/cancel", "cancel releases device");
    fake.BusyForever = false;
    await bridge.SendKeyboardReportAsync(0, [4], default);
    fake.FailPath = "/keyboard/report";
    await Task.Delay(360);
    int failed = fake.Calls.Count(x => x.Path == "/keyboard/report");
    await Task.Delay(360);
    Check(fake.Calls.Count(x => x.Path == "/keyboard/report") == failed, "heartbeat failure stops retrying");
    await Throws<IOException>(() => bridge.SetPadStateAsync("B", 128, 128, 128, 128, default), "lease failure prevents fresh input");
    fake.FailPath = null;
    await bridge.ReleaseAllAsync(default);
}
Console.WriteLine($"InputBridge checks passed: {checks}. HTTP handler simulated; no hardware accessed.");

sealed record Call(string Path, Dictionary<string, string> Values, string Job);
sealed class DeviceHandler : HttpMessageHandler
{
    public readonly ConcurrentQueue<Call> Calls = new();
    public string? FailPath;
    public bool Caps, BusyForever;
    private string _job = "";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
        var values = body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split('=', 2)).ToDictionary(x => WebUtility.UrlDecode(x[0]), x => WebUtility.UrlDecode(x.Length > 1 ? x[1] : ""));
        string path = request.RequestUri!.AbsolutePath;
        string job = request.Headers.TryGetValues("X-Job", out var jobs) ? jobs.Single() : "";
        Calls.Enqueue(new(path, values, job));
        if (path == FailPath) return Response(HttpStatusCode.ServiceUnavailable, "{\"error\":\"test_failure\"}");
        if (path is "/text" or "/tap") _job = job;
        if (path == "/cancel") BusyForever = false;
        if (path == "/status") return Response(HttpStatusCode.OK, JsonSerializer.Serialize(new { boot_id = 7, job = _job, error = "", busy = BusyForever, release_pending = false, leds = Caps ? 2 : 0, gamepad = new { epoch = 9 } }));
        if (path == "/pad/stream") return Response(HttpStatusCode.OK, JsonSerializer.Serialize(new { accepted = true, next_sequence = uint.Parse(values["sequence"]) + 1 }));
        return Response(HttpStatusCode.OK, "{\"accepted\":true}");
    }
    private static HttpResponseMessage Response(HttpStatusCode code, string body) => new(code) { Content = new StringContent(body) };
}
