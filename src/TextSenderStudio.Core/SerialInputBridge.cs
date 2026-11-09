using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace TextSenderStudio.Core;

public interface ISerialKeyboardTransport : IDisposable
{
    bool IsOpen { get; }
    void Open();
    void Close();
    void DiscardInput();
    void WriteLine(string command);
    void Write(byte[] bytes);
    string ReadLine();
}

internal sealed class SerialKeyboardTransport : ISerialKeyboardTransport
{
    private readonly SerialPort _port;
    public SerialKeyboardTransport(string port) => _port = new SerialPort(port, 115200, Parity.None, 8, StopBits.One)
    {
        NewLine = "\n", Encoding = Encoding.ASCII, DtrEnable = true, RtsEnable = true,
        Handshake = Handshake.None, ReadTimeout = 100, WriteTimeout = 1000
    };
    public bool IsOpen => _port.IsOpen;
    public void Open() => _port.Open();
    public void Close() => _port.Close();
    public void DiscardInput() => _port.DiscardInBuffer();
    public void WriteLine(string command) => _port.WriteLine(command);
    public void Write(byte[] bytes) => _port.Write(bytes, 0, bytes.Length);
    public string ReadLine() => _port.ReadLine();
    public void Dispose() => _port.Dispose();
}

/// <summary>UART fallback for TextKeyboard TYPE2/HID firmware. Does not support a gamepad.</summary>
public sealed class SerialInputBridge : IInputBridge
{
    private readonly ISerialKeyboardTransport _serial;
    private readonly int _initializationDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _state = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _heartbeat;
    private CancellationTokenSource? _active;
    private KeyboardReport? _held;
    private string _identity = "";
    private Exception? _leaseError;
    private bool _disposed;

    public SerialInputBridge(AppSettings settings) : this(settings, new SerialKeyboardTransport(
        string.IsNullOrWhiteSpace(settings.SerialPort) ? throw new ArgumentException("请先选择串口。") : settings.SerialPort), 1200) { }

    public SerialInputBridge(AppSettings settings, ISerialKeyboardTransport transport, int initializationDelayMs = 0)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _serial = transport;
        _initializationDelay = Math.Clamp(initializationDelayMs, 0, 5000);
        _heartbeat = HeartbeatAsync();
    }

    public Task<string> StatusAsync(CancellationToken token) => ExecuteAsync(ct =>
    {
        _serial.DiscardInput();
        _serial.WriteLine("PING");
        var identity = ReadResponse(ct, 3000);
        if (!identity.StartsWith("ESP32S3-TEXTKEY/1", StringComparison.Ordinal))
            throw new InvalidOperationException("此串口不是 TextKeyboard 键盘固件。新双 HID 固件请使用 Wi-Fi 连接。");
        _identity = identity;
        _leaseError = null;
        return identity;
    }, token);

    public Task TypeTextAsync(string text, int characterDelayMs, int lineDelayMs, bool clearAutoIndent, CancellationToken token)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (normalized.Any(c => !(c is >= ' ' and <= '~' or '\n' or '\t')))
            throw new ArgumentException("串口键盘固件只支持 ASCII、Tab 和换行。");
        if (characterDelayMs is < 1 or > 1000 || lineDelayMs is < 0 or > 3000)
            throw new ArgumentOutOfRangeException(nameof(characterDelayMs), "串口固件逐字间隔为 1–1000 ms，换行等待为 0–3000 ms。");
        if (normalized.Length == 0) return Task.CompletedTask;
        return ExecuteAsync(ct =>
        {
            RequireCapability("TYPE2=1");
            _held = null;
            foreach (var chunk in normalized.Chunk(8192))
            {
                ct.ThrowIfCancellationRequested();
                var bytes = Encoding.ASCII.GetBytes(chunk);
                _serial.WriteLine(FormattableString.Invariant($"TYPE2 0 {bytes.Length} {characterDelayMs} {lineDelayMs} {(clearAutoIndent ? 1 : 0)}"));
                Expect("READY", ct, 3000);
                _serial.Write(bytes);
                Expect("QUEUED", ct, 3000);
                long expectedMs = 5000;
                foreach (char c in chunk) expectedMs += c == '\n' ? 20 + Math.Max(characterDelayMs, lineDelayMs) + (clearAutoIndent ? 1160 : 0) : 20 + characterDelayMs;
                Expect("DONE", ct, (int)Math.Min(int.MaxValue, expectedMs * 2));
            }
            return true;
        }, token);
    }

    public async Task TapKeyAsync(string keys, int durationMs, CancellationToken token)
    {
        if (durationMs is < 0 or > 60000) throw new ArgumentOutOfRangeException(nameof(durationMs));
        var report = InputBridge.ParseKeyboard(keys);
        try
        {
            await SendKeyboardReportAsync(report.Modifiers, report.Keys, token).ConfigureAwait(false);
            await Task.Delay(Math.Max(20, durationMs), token).ConfigureAwait(false);
        }
        finally { using var timeout = new CancellationTokenSource(3000); await ReleaseAllAsync(timeout.Token).ConfigureAwait(false); }
    }

    public Task SendKeyboardReportAsync(byte modifiers, byte[] keys, CancellationToken token)
    {
        var report = new KeyboardReport(modifiers, keys);
        return ExecuteAsync(ct =>
        {
            RequireCapability("HID=1");
            if (_leaseError is not null) throw new InvalidOperationException("串口直通已因通信错误停止，请重新检查连接。", _leaseError);
            _serial.WriteLine(report.Command());
            Expect("OK HID", ct, 1000);
            _held = report.IsEmpty ? null : report;
            return true;
        }, token);
    }

    public async Task ReleaseAllAsync(CancellationToken token)
    {
        lock (_state) _active?.Cancel();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _held = null;
            if (!_serial.IsOpen || _disposed) return;
            await Task.Run(() =>
            {
                _serial.DiscardInput();
                if (HasCapability("HID=1")) { _serial.WriteLine("HID OFF"); Expect("OK HID", token, 1200); }
                else _serial.Write([0x18]);
            }, token).ConfigureAwait(false);
        }
        catch { CloseTransport(); throw; }
        finally { _gate.Release(); }
    }

    public Task PressPadAsync(string buttons, int durationMs, CancellationToken token) => Task.FromException(new NotSupportedException("UART 旧键盘固件不支持 Pro 手柄，请切换到新版 Wi-Fi 双 HID 固件。"));
    public Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token) => PressPadAsync(buttons, 0, token);
    public Task ConfigureWifiAsync(string ssid, string password, CancellationToken token) => Task.FromException(new NotSupportedException("UART 旧键盘固件不支持 Wi-Fi 配网，请连接新版固件的热点。"));

    private bool HasCapability(string capability) => _identity.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(capability, StringComparer.Ordinal);
    private void RequireCapability(string capability)
    {
        if (!HasCapability(capability)) throw new NotSupportedException($"当前串口固件未提供 {capability}，请使用配套 TextKeyboard v1.4 或更新的键盘固件。");
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, T> action, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_state) _active = linked;
            return await Task.Run(() =>
            {
                var ct = linked.Token;
                ct.ThrowIfCancellationRequested();
                if (!_serial.IsOpen)
                {
                    _serial.Open();
                    if (ct.WaitHandle.WaitOne(_initializationDelay)) ct.ThrowIfCancellationRequested();
                    _serial.DiscardInput();
                    _serial.WriteLine("PING");
                    _identity = ReadResponse(ct, 3000);
                    if (!_identity.StartsWith("ESP32S3-TEXTKEY/1", StringComparison.Ordinal)) throw new InvalidOperationException("所选串口不是 TextKeyboard 固件。");
                }
                return action(ct);
            }, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await Task.Run(CancelAndDrain).ConfigureAwait(false);
            throw;
        }
        catch { CloseTransport(); throw; }
        finally { lock (_state) _active = null; _gate.Release(); }
    }

    private void CancelAndDrain()
    {
        _held = null;
        if (!_serial.IsOpen) return;
        try
        {
            _serial.Write([0x18]);
            using var timeout = new CancellationTokenSource(1800);
            while (true)
            {
                var reply = ReadResponse(timeout.Token, 1800);
                if (reply is "CANCELLED" or "DONE" or "OK HID") break;
            }
            _serial.DiscardInput();
            if (HasCapability("HID=1")) { _serial.WriteLine("HID OFF"); Expect("OK HID", timeout.Token, 1000); }
        }
        catch { CloseTransport(); }
    }

    private string ReadResponse(CancellationToken token, int timeoutMs)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var line = _serial.ReadLine().Trim();
                if (line.Length != 0) return line;
            }
            catch (TimeoutException) { }
        }
        throw new TimeoutException("等待串口固件响应超时。");
    }
    private void Expect(string expected, CancellationToken token, int timeoutMs)
    {
        var actual = ReadResponse(token, timeoutMs);
        if (actual == "CANCELLED") throw new OperationCanceledException("设备已取消操作。", token);
        if (actual != expected) throw new IOException(actual == "ERR HID" ? "USB 键盘未就绪，请检查连接目标主机的 USB/OTG 线。" : "串口固件未确认操作，已停止发送。");
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (!await _gate.WaitAsync(0, _lifetime.Token).ConfigureAwait(false)) continue;
                try
                {
                    if (_held is null || !_serial.IsOpen) continue;
                    await Task.Run(() => { _serial.WriteLine(_held.Command()); Expect("OK HID", _lifetime.Token, 1000); }, _lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _leaseError = ex; _held = null;
                    try { _serial.WriteLine("HID OFF"); } catch { }
                    CloseTransport();
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void CloseTransport() { _held = null; _identity = ""; try { _serial.Close(); } catch { } }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await _heartbeat.ConfigureAwait(false); } catch { }
        try { using var timeout = new CancellationTokenSource(3500); await ReleaseAllAsync(timeout.Token).ConfigureAwait(false); } catch { }
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _disposed = true; CloseTransport(); _serial.Dispose(); }
        finally { _gate.Release(); _lifetime.Dispose(); }
    }
}
