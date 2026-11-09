using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using TextSenderStudio.Core;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Until(Func<bool> value) { var timer = Stopwatch.StartNew(); while (!value()) { if (timer.ElapsedMilliseconds > 2500) throw new Exception("Test timeout"); await Task.Delay(5); } }
async Task Throws<T>(Func<Task> action, string message) where T : Exception { try { await action(); } catch (T) { checks++; return; } throw new Exception(message); }

var bridge = new FakeBridge();
await using (var pump = new PassthroughReportPump(bridge))
{
    Check(!pump.Submit(new KeyboardReport(0, 4)), "disabled pump rejects reports");
    pump.Resume();
    Check(pump.Submit(new KeyboardReport(0, 4)), "enqueue down");
    Check(pump.Submit(new KeyboardReport(0)), "enqueue up");
    await Until(() => bridge.Reports.Count == 2);
    Check(bridge.Reports.ToArray().SequenceEqual(new[] { "0:4,0,0,0,0,0", "0:0,0,0,0,0,0" }), "short down/up order preserved");
    await pump.PauseAsync();
    Check(bridge.Releases == 1 && !pump.Submit(new KeyboardReport(0, 5)), "pause releases and disables");
    pump.Resume();bridge.Block = true;
    pump.Submit(new KeyboardReport(0, 6));
    await Until(() => bridge.BlockEntered);
    pump.Submit(new KeyboardReport(0, 7));
    await pump.PauseAsync();
    Check(!bridge.Reports.Any(x => x.Contains(":7,")), "stale queued down dropped on pause");
    bridge.Block = false;bridge.Throw = true;
    var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    pump.Failed += _ => failed.TrySetResult();
    pump.Resume();pump.Submit(new KeyboardReport(0, 8));
    await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Check(!pump.Submit(new KeyboardReport(0, 9)), "send failure pauses pump");
}

var keys = new KeyboardReportState();
Check(keys.HandleKeyMessage(0x100, new IntPtr(0x41), new IntPtr(0x1e << 16), out var first, out _) && first.Keys[0] == 4, "A physical mapping");
Check(keys.HandleKeyMessage(0x100, new IntPtr(0x42), new IntPtr(0x30 << 16), out var chord, out _) && chord.Keys.Take(2).SequenceEqual(new byte[] { 4, 5 }), "multi-key report");
Check(keys.HandleKeyMessage(0x100, new IntPtr(0x11), new IntPtr(0x1d << 16), out var control, out _) && control.Modifiers == 1 && control.Keys[1] == 5, "modifier with chord");
Check(keys.Clear().IsEmpty, "focus clear empties state");

var serial = new FakeSerial();
await using (var input = new SerialInputBridge(new AppSettings { SerialPort = "TEST" }, serial))
{
    Check(!serial.IsOpen, "serial lazy open");
    var status = await input.StatusAsync(default);
    Check(status.Contains("TYPE2=1") && serial.IsOpen, "identity handshake");
    await input.TypeTextAsync("ABC\r\n123", 30, 150, true, default);
    Check(serial.Commands.Contains("TYPE2 0 7 30 150 1"), "TYPE2 immediate delay and normalized size");
    Check(Encoding.ASCII.GetString(serial.Payloads.Last()) == "ABC\n123", "normalized payload");
    await input.TypeTextAsync(new string('A', 8200), 1, 0, false, default);
    Check(serial.Payloads.TakeLast(2).Select(x => x.Length).SequenceEqual(new[] { 8192, 8 }), "payload chunks obey firmware capacity");
    await Throws<ArgumentException>(() => input.TypeTextAsync("中文", 30, 150, false, default), "Unicode rejected");
    await Throws<ArgumentOutOfRangeException>(() => input.TypeTextAsync("A", 0, 0, false, default), "old firmware gap limits");
    await input.SendKeyboardReportAsync(2, [4, 5], default);
    Check(serial.Commands.Contains("HID 2 4 5 0 0 0 0"), "raw multi-key report");
    var initialReports = serial.Commands.Count(x => x == "HID 2 4 5 0 0 0 0");
    await Until(() => serial.Commands.Count(x => x == "HID 2 4 5 0 0 0 0") > initialReports);
    Check(true, "held state heartbeat renews watchdog");
    await input.ReleaseAllAsync(default);
    Check(serial.Commands.Last() == "HID OFF", "explicit release");
    await input.TapKeyAsync("CTRL+ENTER", 0, default);
    Check(serial.Commands.Contains("HID 1 40 0 0 0 0 0") && serial.Commands.Last() == "HID OFF", "tap uses chord and releases");
    await Throws<NotSupportedException>(() => input.PressPadAsync("A", 100, default), "serial pad explicitly unsupported");
    await Throws<NotSupportedException>(() => input.ConfigureWifiAsync("demo", "password", default), "serial WiFi explicitly unsupported");
    serial.HoldDone = true;
    using var stop = new CancellationTokenSource();
    var pending = input.TypeTextAsync("STOP TEST", 30, 150, false, stop.Token);
    await Until(() => serial.WaitingDone);
    stop.Cancel();
    await Throws<OperationCanceledException>(() => pending, "typing cancellation propagated");
    Check(serial.CancelCount > 0 && serial.Commands.Last() == "HID OFF", "typing cancellation sends control-X and releases");
}
Check(!serial.IsOpen && serial.Disposed, "serial disposal closes port");
Console.WriteLine($"Input checks passed: {checks}. Mock bridge/serial only; no hook or physical port opened.");

sealed class FakeBridge : IInputBridge
{
    public readonly ConcurrentQueue<string> Reports = new();
    public int Releases;
    public volatile bool Block, BlockEntered, Throw;
    public async Task SendKeyboardReportAsync(byte modifiers, byte[] keys, CancellationToken token)
    {
        Reports.Enqueue(modifiers + ":" + string.Join(',', keys));
        if (Throw) throw new IOException("synthetic transport error");
        if (Block) { BlockEntered = true; await Task.Delay(Timeout.Infinite, token); }
    }
    public Task ReleaseAllAsync(CancellationToken token) { Interlocked.Increment(ref Releases); return Task.CompletedTask; }
    public Task<string> StatusAsync(CancellationToken token) => Task.FromResult("test");
    public Task TypeTextAsync(string text, int c, int l, bool clear, CancellationToken token) => Task.CompletedTask;
    public Task TapKeyAsync(string keys, int durationMs, CancellationToken token) => Task.CompletedTask;
    public Task PressPadAsync(string buttons, int durationMs, CancellationToken token) => Task.CompletedTask;
    public Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token) => Task.CompletedTask;
    public Task ConfigureWifiAsync(string ssid, string password, CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class FakeSerial : ISerialKeyboardTransport
{
    public bool IsOpen { get; private set; }
    public bool Disposed;
    public volatile bool HoldDone, WaitingDone;
    public int CancelCount;
    public readonly ConcurrentQueue<string> Commands = new(), Replies = new();
    public readonly ConcurrentQueue<byte[]> Payloads = new();
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
    public void DiscardInput() { while (Replies.TryDequeue(out _)) { } }
    public void WriteLine(string command)
    {
        Commands.Enqueue(command);
        if (command == "PING") Replies.Enqueue("ESP32S3-TEXTKEY/1 TYPE2=1 HID=1");
        else if (command.StartsWith("TYPE2 ")) Replies.Enqueue("READY");
        else if (command.StartsWith("HID ")) Replies.Enqueue("OK HID");
        else throw new IOException("Unexpected command");
    }
    public void Write(byte[] bytes)
    {
        if (bytes.Length == 1 && bytes[0] == 0x18) { Interlocked.Increment(ref CancelCount); Replies.Enqueue("CANCELLED"); return; }
        Payloads.Enqueue(bytes);Replies.Enqueue("QUEUED");
        if (HoldDone) WaitingDone = true; else Replies.Enqueue("DONE");
    }
    public string ReadLine() { if (Replies.TryDequeue(out var response)) return response; Thread.Sleep(5); throw new TimeoutException(); }
    public void Dispose() { IsOpen = false; Disposed = true; }
}
