using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message); checks++;
}
async Task Until(Func<bool> condition)
{
    var watch = Stopwatch.StartNew();
    while (!condition() && watch.ElapsedMilliseconds < 3000) await Task.Delay(5);
    if (!condition()) throw new TimeoutException("Direct control fixture timed out.");
}

var settings = new DirectControlSettings();
Check(ControllerCatalog.Targets.Count == 40 && ControllerCatalog.Targets.Select(t => t.Name).Distinct().Count() == 40,
    "catalog covers fourteen buttons, eight D-pad directions and both eight-way sticks with reset");
foreach (var name in ControllerCatalog.ButtonNames)
{
    var state = ControllerMapper.FromTargets([name]);
    Check(InputBridge.ParsePad(state.ButtonNames).Buttons == InputBridge.ParsePad(name).Buttons && state.Hat == 15,
        "controller catalog matches firmware button encoding: " + name);
}
for (int i = 0; i < 8; i++)
    Check(ControllerMapper.FromTargets([ControllerCatalog.Directions[i]]).Hat == i, "D-pad direction encoding: " + ControllerCatalog.Directions[i]);
Check(ControllerMapper.FromTargets(["A", "L", "R", "UP", "LEFT"]) == new PadState(52, 7, 128, 128, 128, 128), "simultaneous buttons combine with diagonal direction");
Check(ControllerMapper.FromTargets(["UP", "DOWN", "LEFT"]).Hat == 6 && ControllerMapper.FromTargets(["UP", "DOWN", "LEFT", "RIGHT"]).Hat == 15,
    "opposite directions cancel per axis");
Check(ControllerMapper.FromTargets(["LS_UP", "LS_LEFT", "RS_DOWNRIGHT"]) == new PadState(0, 15, 0, 0, 255, 255), "independent stick diagonals retain all four axes");
Check(ControllerMapper.FromTargets(["LS_UP", "LS_DOWN", "LS_RIGHT", "RS_UP", "RS_RESET"]) == new PadState(0, 15, 255, 128, 128, 128), "stick opposites and explicit reset return the correct axes to center");
Check(ControllerMapper.FromKeys([76, 81, 87, 65], settings) == new PadState(1028, 15, 0, 0, 128, 128), "EasyCon default keyboard mapping supports chords and left stick");
settings.KeyBindings["A"] = 70; settings.KeyBindings["ZL"] = 70;
Check(ControllerMapper.FromKeys([70, 70], settings).Buttons == 68, "one physical key may map to multiple controller buttons without duplicated presses");
settings.KeyBindings["A"] = 0;
Check(ControllerMapper.FromKeys([0, 70], settings).Buttons == 64, "zero virtual key means unbound");
var restored = JsonSerializer.Deserialize<DirectControlSettings>(JsonSerializer.Serialize(settings))!;
Check(restored.KeyBindings["A"] == 0 && restored.KeyBindings["ZL"] == 70 && restored.SwapFaceButtons, "custom keyboard mapping and layout preference survive settings serialization");
settings = new();
Check(ControllerMapper.FromXInput(new(0x1000, 0, 0, 0, 0, 0, 0), settings).Buttons == 2, "physical south button defaults to Switch B position");
settings.SwapFaceButtons = false;
Check(ControllerMapper.FromXInput(new(0x1000, 0, 0, 0, 0, 0, 0), settings).Buttons == 4, "face button letter mapping can be selected");
Check(ControllerMapper.FromXInput(new(0xF3F0, 26, 26, 0, 0, 0, 0), settings).Buttons == 4095, "XInput face shoulder trigger stick-click plus and minus all map");
Check((ControllerMapper.FromXInput(new(0xFFFF, 255, 255, 0, 0, 0, 0), settings).Buttons & (4096 | 8192)) == 0, "standard XInput never invents HOME or CAPTURE buttons");
Check(ControllerMapper.FromXInput(new(0, 25, 26, 0, 0, 0, 0), settings).Buttons == 128, "trigger threshold applies strictly above configured value");
Check(ControllerMapper.FromXInput(new(0, 0, 0, 0, 7849, 0, 0), settings) == PadState.Neutral, "dead-zone boundary produces neutral sticks");
Check(ControllerMapper.FromXInput(new(0, 0, 0, short.MinValue, short.MaxValue, short.MaxValue, short.MinValue), settings) == new PadState(0, 15, 0, 0, 255, 255), "XInput axis endpoints and vertical inversion are correct");
Check(!XInputReader.TryRead(-1, out _) && !XInputReader.TryRead(4, out _), "invalid gamepad slots are rejected before opening any physical input");
var left = new PadState(4, 7, 0, 128, 128, 64);
var right = new PadState(32, 3, 255, 128, 128, 128);
Check(PadState.Merge(left, right) == new PadState(36, 15, 128, 128, 128, 64), "keyboard screen and gamepad sources merge chords and cancel full opposite axes exactly");
Check(PadState.Merge(left, PadState.Neutral) == left, "neutral source does not alter analogue input from another source");

var recorder = new ControllerRecorder();
var chord = new PadState(36, 7, 61, 192, 200, 17);
var changed = chord with { Buttons = 32, LX = 73, RY = 25 };
recorder.Start(); recorder.RecordAt(chord, 0); recorder.RecordAt(chord, 1); recorder.RecordAt(changed, 2); recorder.RecordAt(PadState.Neutral, 4);
string script = recorder.StopAt(6);
Check(!recorder.IsRecording && script.Split('\n').Count(line => line.StartsWith("PAD_STATE")) == 4
    && script.Split('\n').Count(line => line.StartsWith("WAIT 2")) == 2, "recorder records changed reports once with elapsed millisecond waits");
var engine = new StudioScriptEngine();
Check(!engine.Validate(script).Any(d => d.IsError), "recorded analogue and chord script compiles through the actual engine");
var playback = new Playback();
await engine.RunAsync(script, playback, CancellationToken.None);
Check(playback.States.SequenceEqual(new[] { PadState.Neutral, chord, changed, PadState.Neutral, PadState.Neutral }), "recorded script replays exact report states through a fake host and ends neutral");
recorder.Start(chord); string released = recorder.StopAt(3);
Check(released.Contains("WAIT 3") && released.TrimEnd().EndsWith("PAD_STATE \"\", 128, 128, 128, 128"), "stopping a recording with held controls appends its final release");
recorder.RecordAt(changed, 9);
Check(recorder.StopAt(20) == released, "stopped recording ignores later reports and repeated stop calls");
recorder.Start(); recorder.RecordAt(chord, (long)int.MaxValue + 3);
var longWait = recorder.StopAt((long)int.MaxValue + 3);
Check(longWait.Contains("WAIT 2147483647") && longWait.Contains("WAIT 3") && !engine.Validate(longWait).Any(d => d.IsError), "very long recording gaps split into valid signed millisecond waits without overflow");
recorder.Start(); recorder.RecordAt(chord, 5); recorder.RecordAt(changed, 1); var backwards = recorder.StopAt(3);
Check(!backwards.Contains("WAIT -"), "backwards sampling timestamps never produce negative waits");

var bridge = new PadBridge();
await using (var pump = new DirectControlPump(bridge))
{
    var confirmed = new ConcurrentQueue<PadState>(); pump.Confirmed += confirmed.Enqueue;
    Check(pump.Submit(chord) && pump.Submit(PadState.Neutral), "pump accepts quick press and release");
    await Until(() => confirmed.Count == 2);
    Check(bridge.Events.Select(e => e.State).SequenceEqual(new[] { chord, PadState.Neutral }), "quick press and release keep their order instead of collapsing");
    await pump.PauseAsync();
    Check(bridge.Events.Last().Kind == "release" && bridge.MaxActive == 1, "pause releases through the same serialized worker");
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bridge.BeforeSend = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
    pump.Submit(chord); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); pump.Submit(changed);
    var pause = pump.PauseAsync();
    Check(!pump.Submit(left), "new reports are rejected until pause release barrier completes");
    await pause.WaitAsync(TimeSpan.FromSeconds(3));
    Check(bridge.Events.Count(e => e.State == changed) == 0 && confirmed.Last() == PadState.Neutral, "pause cancels active send and drops stale queued input before confirming neutral");
    bridge.BeforeSend = null; pump.Submit(changed); await Until(() => confirmed.Last() == changed);
    await pump.PauseAsync();
    Check(bridge.Events.Last().Kind == "release" && bridge.MaxActive == 1, "new submission after a completed pause resumes without overlapping reports");
}

var failureBridge = new PadBridge();
await using (var pump = new DirectControlPump(failureBridge))
{
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var failNow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    pump.Failed += _ => failed.TrySetResult();
    failureBridge.BeforeSend = async (_, _) => { entered.TrySetResult(); await failNow.Task; throw new IOException("Simulated send failure racing with pause"); };
    pump.Submit(chord); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var pause = pump.PauseAsync(); failNow.TrySetResult();
    await Task.WhenAll(pause, failed.Task).WaitAsync(TimeSpan.FromSeconds(3));
    Check(failureBridge.Events.Last().Kind == "release" && !pump.Submit(changed), "transport failure racing with pause completes its release barrier and stops further sends");
}

var overflowBridge = new PadBridge();
await using (var pump = new DirectControlPump(overflowBridge))
{
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    pump.Failed += _ => failed.TrySetResult();
    overflowBridge.BeforeSend = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
    pump.Submit(chord); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    bool accepted = true;
    for (int i = 0; i < 256; i++) accepted &= pump.Submit(i % 2 == 0 ? left : right);
    Check(accepted, "bounded queue accepts its configured 256 pending transitions");
    Check(!pump.Submit(changed), "queue overflow rejects input instead of losing button transitions silently");
    await failed.Task.WaitAsync(TimeSpan.FromSeconds(3)); await pump.PauseAsync().WaitAsync(TimeSpan.FromSeconds(3));
    Check(overflowBridge.Events.Last().Kind == "release" && !pump.Submit(chord), "overflow cancels blocked transport and finishes with neutral release");
}

var releaseFailure = new PadBridge { BeforeRelease = _ => throw new IOException("Simulated release failure") };
var failedPump = new DirectControlPump(releaseFailure);
int failures = 0; failedPump.Failed += _ => Interlocked.Increment(ref failures);
failedPump.Submit(chord); await Until(() => releaseFailure.Events.Count > 0);
try { await failedPump.PauseAsync(); throw new Exception("Release failure was hidden"); } catch (IOException) { }
try { await failedPump.PauseAsync(); throw new Exception("Repeated release failure was hidden"); } catch (IOException) { }
await Until(() => failures == 1);
Check(failures == 1 && !failedPump.Submit(changed), "failed release faults input once without recursive failure callbacks");
try { await failedPump.DisposeAsync(); } catch (IOException) { }

var protocol = new PadProtocol();
using (var http = new HttpClient(protocol))
await using (var realBridge = new InputBridge(new AppSettings(), http))
await using (var pump = new DirectControlPump(realBridge))
{
    pump.Submit(chord);
    await Until(() => protocol.Calls.Any(call => call.Path == "/pad/stream" && call.Frames == ""));
    Check(protocol.Calls.Count(call => call.Path == "/pad/stream" && call.Frames != "") == 1, "held direct input uses the existing bridge lease heartbeat without repeating the action frame");
    await pump.PauseAsync(); var count = protocol.Calls.Count;
    await Task.Delay(350);
    Check(protocol.Calls.Count == count && protocol.Calls.Last().Path == "/cancel", "pause clears the pad lease so heartbeat cannot resurrect a held report");
}
Console.WriteLine($"Direct control checks passed: {checks}. Mock bridge, script host and HTTP handler only; no hardware accessed.");

sealed class Playback : IScriptActions, IStatefulScriptActions
{
    public List<PadState> States { get; } = [];
    public IReadOnlyList<string> FunctionNames => [];
    public void Log(string message) { }
    public Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token)
    { token.ThrowIfCancellationRequested(); var parsed = InputBridge.ParsePad(buttons); States.Add(new(parsed.Buttons, parsed.Dpad, lx, ly, rx, ry)); return Task.CompletedTask; }
    public Task PadAsync(string buttons, int durationMs, CancellationToken token) => throw new InvalidOperationException("Unexpected tap");
    public Task KeyAsync(string keys, int durationMs, CancellationToken token) => throw new InvalidOperationException("Unexpected keyboard");
    public Task TypeAsync(string text, CancellationToken token) => throw new InvalidOperationException("Unexpected typing");
    public Task<double> MatchAsync(string labelName, CancellationToken token) => throw new InvalidOperationException("Unexpected capture");
    public Task CallAsync(string functionName, CancellationToken token) => throw new InvalidOperationException("Unexpected external action");
}

sealed class PadBridge : IInputBridge
{
    public ConcurrentQueue<(string Kind, PadState State)> Events { get; } = new();
    public Func<PadState, CancellationToken, Task>? BeforeSend;
    public Func<CancellationToken, Task>? BeforeRelease;
    private int _active;
    public int MaxActive;
    private void Enter() { int active = Interlocked.Increment(ref _active); MaxActive = Math.Max(MaxActive, active); }
    public async Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token)
    {
        Enter();
        try
        {
            var parsed = InputBridge.ParsePad(buttons); var state = new PadState(parsed.Buttons, parsed.Dpad, lx, ly, rx, ry);
            if (BeforeSend != null) await BeforeSend(state, token);
            token.ThrowIfCancellationRequested(); Events.Enqueue(("state", state));
        }
        finally { Interlocked.Decrement(ref _active); }
    }
    public async Task ReleaseAllAsync(CancellationToken token)
    {
        Enter();
        try { if (BeforeRelease != null) await BeforeRelease(token); token.ThrowIfCancellationRequested(); Events.Enqueue(("release", PadState.Neutral)); }
        finally { Interlocked.Decrement(ref _active); }
    }
    public Task<string> StatusAsync(CancellationToken token) => throw new InvalidOperationException("Unexpected status");
    public Task TypeTextAsync(string text, int c, int l, bool clear, CancellationToken token) => throw new InvalidOperationException("Unexpected typing");
    public Task TapKeyAsync(string keys, int durationMs, CancellationToken token) => throw new InvalidOperationException("Unexpected key");
    public Task PressPadAsync(string buttons, int durationMs, CancellationToken token) => throw new InvalidOperationException("Unexpected tap");
    public Task SendKeyboardReportAsync(byte modifiers, byte[] keys, CancellationToken token) => throw new InvalidOperationException("Unexpected keyboard report");
    public Task ConfigureWifiAsync(string ssid, string password, CancellationToken token) => throw new InvalidOperationException("Unexpected Wi-Fi setup");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class PadProtocol : HttpMessageHandler
{
    public ConcurrentQueue<(string Path, string Frames)> Calls { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
        var values = body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair.ElementAtOrDefault(1) ?? ""));
        var path = request.RequestUri!.AbsolutePath; Calls.Enqueue((path, values.GetValueOrDefault("frames", "")));
        string response = path switch
        {
            "/status" => "{\"gamepad\":{\"epoch\":9}}",
            "/pad/stream" => JsonSerializer.Serialize(new { accepted = true, next_sequence = uint.Parse(values["sequence"]) + 1 }),
            "/cancel" => "{\"accepted\":true}",
            _ => throw new InvalidOperationException("Unexpected mock endpoint: " + path)
        };
        return new(HttpStatusCode.OK) { Content = new StringContent(response) };
    }
}
