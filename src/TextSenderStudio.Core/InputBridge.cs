using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace TextSenderStudio.Core;

/// <summary>TextSender Studio Wi-Fi firmware. Mutating requests are never retried.</summary>
public sealed class InputBridge : IInputBridge
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly Uri _baseUri;
    private readonly int _timeout;
    private readonly SemaphoreSlim _reports = new(1, 1);
    private readonly SemaphoreSlim _jobs = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _heartbeat;
    private string? _keyboardSession, _padSession;
    private uint _keyboardSequence, _padSequence, _padEpoch;
    private byte _modifiers;
    private byte[] _keys = [];
    private Exception? _leaseFailure;
    private bool _disposed;

    public InputBridge(AppSettings settings) : this(settings, new HttpClient(), true) { }
    public InputBridge(AppSettings settings, HttpClient client) : this(settings, client, false) { }
    private InputBridge(AppSettings settings, HttpClient client, bool ownsClient)
    {
        if (!settings.Transport.Equals("WiFi", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("此连接器使用 Wi-Fi；串口请使用串口连接器。");
        _http = client; _ownsClient = ownsClient;
        _baseUri = new Uri(settings.EspBaseUrl.TrimEnd('/') + "/");
        _timeout = Math.Max(100, settings.RequestTimeoutMs);
        _heartbeat = HeartbeatAsync();
    }

    public Task<string> StatusAsync(CancellationToken token) => RequestAsync(HttpMethod.Get, "status", null, null, token);

    private async Task<string> RequestAsync(HttpMethod method, string path, Dictionary<string, string>? values, string? job, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        if (values is not null) request.Content = new FormUrlEncodedContent(values);
        if (job is not null) request.Headers.Add("X-Job", job);
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ESP32 {(int)response.StatusCode}: {body}", null, response.StatusCode);
        return body;
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonElement Pad(JsonElement root) => root.GetProperty("gamepad");
    private static bool Flag(JsonElement root, string property) => root.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;

    private void CheckLease()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_leaseFailure is not null)
            throw new IOException("输入连接中断，已停止续租。请先点击停止/释放，再重新连接。", _leaseFailure);
    }

    public async Task TypeTextAsync(string text, int characterDelayMs, int lineDelayMs, bool clearAutoIndent, CancellationToken token)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.Any(c => c != '\n' && c != '\t' && (c < 32 || c > 126)))
            throw new ArgumentException("USB 文本输入支持 ASCII、Tab 和换行；请移除不支持的字符。");
        if (characterDelayMs is < 0 or > 60000 || lineDelayMs is < 0 or > 60000)
            throw new ArgumentOutOfRangeException(nameof(characterDelayMs), "等待范围为 0–60000 毫秒。");
        await _jobs.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckLease();
            using (var status = JsonDocument.Parse(await StatusAsync(token).ConfigureAwait(false)))
                if (status.RootElement.TryGetProperty("leds", out var leds) && (leds.GetInt32() & 2) != 0)
                    await TapCoreAsync("CAPSLOCK", 30, token).ConfigureAwait(false);
            if (!clearAutoIndent)
            {
                for (int offset = 0; offset < text.Length; offset += 8192)
                    await TypeChunkAsync(text.Substring(offset, Math.Min(8192, text.Length - offset)), characterDelayMs, lineDelayMs, token).ConfigureAwait(false);
            }
            else
            {
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    for (int offset = 0; offset < lines[i].Length; offset += 8192)
                        await TypeChunkAsync(lines[i].Substring(offset, Math.Min(8192, lines[i].Length - offset)), characterDelayMs, lineDelayMs, token).ConfigureAwait(false);
                    if (i == lines.Length - 1) break;
                    await TypeChunkAsync("\n", characterDelayMs, lineDelayMs, token).ConfigureAwait(false);
                    await TapCoreAsync("SHIFT+HOME", 20, token).ConfigureAwait(false);
                    await TapCoreAsync("BACKSPACE", 20, token).ConfigureAwait(false);
                }
            }
        }
        catch { await BestEffortCancelAsync().ConfigureAwait(false); throw; }
        finally { _jobs.Release(); }
    }

    private async Task TypeChunkAsync(string text, int gap, int line, CancellationToken token)
    {
        if (text.Length == 0) return;
        string job = Guid.NewGuid().ToString("N");
        using var status = JsonDocument.Parse(await StatusAsync(token).ConfigureAwait(false));
        uint boot = status.RootElement.GetProperty("boot_id").GetUInt32();
        await RequestAsync(HttpMethod.Post, "text", new() { ["plain"] = text, ["gap"] = Number(gap), ["line"] = Number(line), ["layout"] = "us" }, job, token).ConfigureAwait(false);
        long expected = (2L * gap + 10) * text.Length + (long)line * text.Count(c => c == '\n') + 10000;
        await WaitJobAsync(job, boot, false, expected, token).ConfigureAwait(false);
    }

    public async Task TapKeyAsync(string keys, int durationMs, CancellationToken token)
    {
        await _jobs.WaitAsync(token).ConfigureAwait(false);
        try { CheckLease(); await TapCoreAsync(keys, durationMs, token).ConfigureAwait(false); }
        catch { await BestEffortCancelAsync().ConfigureAwait(false); throw; }
        finally { _jobs.Release(); }
    }

    private async Task TapCoreAsync(string keys, int durationMs, CancellationToken token)
    {
        var report = ParseKeyboard(keys);
        if (report.Keys.Length != 1) throw new ArgumentException("单次按键需要一个普通键，可同时包含 Ctrl/Shift/Alt/Win。多键请使用键盘直通。");
        if (durationMs is < 0 or > 60000) throw new ArgumentOutOfRangeException(nameof(durationMs));
        string job = Guid.NewGuid().ToString("N");
        using var status = JsonDocument.Parse(await StatusAsync(token).ConfigureAwait(false));
        uint boot = status.RootElement.GetProperty("boot_id").GetUInt32();
        await RequestAsync(HttpMethod.Post, "tap", new() { ["key"] = Number(report.Keys[0]), ["mod"] = Number(report.Modifiers), ["gap"] = Number(durationMs), ["line"] = "0" }, job, token).ConfigureAwait(false);
        await WaitJobAsync(job, boot, false, durationMs * 2L + 10000, token).ConfigureAwait(false);
    }

    public async Task PressPadAsync(string buttons, int durationMs, CancellationToken token)
    {
        if (durationMs is < 0 or > 60000) throw new ArgumentOutOfRangeException(nameof(durationMs));
        await _jobs.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckLease();
            await SetPadStateAsync(buttons, 128, 128, 128, 128, token).ConfigureAwait(false);
            await Task.Delay(Math.Max(1, durationMs), token).ConfigureAwait(false);
            await SetPadStateAsync("", 128, 128, 128, 128, token).ConfigureAwait(false);
        }
        catch { await BestEffortCancelAsync().ConfigureAwait(false); throw; }
        finally { _jobs.Release(); }
    }

    public async Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token)
    {
        var (mask, dpad) = ParsePad(buttons);
        await _reports.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckLease();
            if (_padSession is null)
            {
                using var status = JsonDocument.Parse(await StatusAsync(token).ConfigureAwait(false));
                _padEpoch = Pad(status.RootElement).GetProperty("epoch").GetUInt32();
                _padSession = Guid.NewGuid().ToString("N"); _padSequence = 0;
            }
            string frame = $"{mask},{dpad},{lx},{ly},{rx},{ry},16";
            await PostPadFrameAsync(frame, token).ConfigureAwait(false);
        }
        catch (Exception ex) { _padSession = null; _keyboardSession = null; _leaseFailure = ex; throw; }
        finally { _reports.Release(); }
    }

    private async Task PostPadFrameAsync(string frames, CancellationToken token)
    {
        string reply = await RequestAsync(HttpMethod.Post, "pad/stream", new() { ["session"] = _padSession!, ["sequence"] = Number(_padSequence), ["epoch"] = Number(_padEpoch), ["frames"] = frames }, null, token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(reply);
        if (!Flag(json.RootElement, "accepted") || json.RootElement.GetProperty("next_sequence").GetUInt32() != _padSequence + 1)
            throw new IOException("ESP32 返回无效的手柄序号确认。");
        ++_padSequence;
    }

    public async Task SendKeyboardReportAsync(byte modifiers, byte[] keys, CancellationToken token)
    {
        if (keys.Length > 6 || keys.Any(k => k is > 0 and < 4 or > 0xe7)) throw new ArgumentException("键盘报告最多 6 个普通键，修饰键独立计算。");
        await _reports.WaitAsync(token).ConfigureAwait(false);
        try
        {
            CheckLease();
            _keyboardSession ??= Guid.NewGuid().ToString("N");
            _modifiers = modifiers; _keys = keys.ToArray();
            await PostKeyboardAsync(token).ConfigureAwait(false);
            if (modifiers == 0 && !keys.Any(k => k != 0)) { _keyboardSession = null; _keyboardSequence = 0; }
        }
        catch (Exception ex) { _keyboardSession = null; _padSession = null; _leaseFailure = ex; throw; }
        finally { _reports.Release(); }
    }

    private async Task PostKeyboardAsync(CancellationToken token)
    {
        var values = new Dictionary<string, string> { ["session"] = _keyboardSession!, ["sequence"] = Number(_keyboardSequence), ["mod"] = Number(_modifiers) };
        for (int i = 0; i < 6; i++) values["k" + i] = Number(i < _keys.Length ? _keys[i] : 0);
        string reply = await RequestAsync(HttpMethod.Post, "keyboard/report", values, null, token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(reply);
        if (!Flag(json.RootElement, "accepted")) throw new IOException("ESP32 未确认键盘报告。");
        ++_keyboardSequence;
    }

    private async Task HeartbeatAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                await _reports.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                try
                {
                    if (_keyboardSession is not null) await PostKeyboardAsync(_lifetime.Token).ConfigureAwait(false);
                    if (_padSession is not null) await PostPadFrameAsync("", _lifetime.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!_lifetime.IsCancellationRequested)
                { _leaseFailure = ex; _keyboardSession = null; _padSession = null; }
                finally { _reports.Release(); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task WaitJobAsync(string job, uint boot, bool gamepad, long budgetMs, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds <= budgetMs)
        {
            token.ThrowIfCancellationRequested();
            using var json = JsonDocument.Parse(await StatusAsync(token).ConfigureAwait(false));
            var root = json.RootElement;
            if (root.GetProperty("boot_id").GetUInt32() != boot) throw new IOException("ESP32 已重启，任务结果未知。");
            var status = gamepad ? Pad(root) : root;
            if (status.GetProperty("job").GetString() != job) throw new IOException("ESP32 当前任务已变化，停止等待。");
            string error = status.GetProperty("error").GetString() ?? "";
            if (error.Length != 0) throw new IOException("ESP32: " + error);
            if (!Flag(status, "busy") && !Flag(status, "release_pending")) return;
            await Task.Delay(20, token).ConfigureAwait(false);
        }
        throw new TimeoutException("ESP32 输入任务超时；未重发指令。");
    }

    public async Task ReleaseAllAsync(CancellationToken token)
    {
        await _reports.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _keyboardSession = null; _padSession = null; _keyboardSequence = _padSequence = 0;
            await RequestAsync(HttpMethod.Post, "cancel", [], null, token).ConfigureAwait(false);
            _leaseFailure = null;
        }
        finally { _reports.Release(); }
    }

    private async Task BestEffortCancelAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await ReleaseAllAsync(cancellation.Token).ConfigureAwait(false); } catch { }
    }

    public async Task ConfigureWifiAsync(string ssid, string password, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ssid) || System.Text.Encoding.UTF8.GetByteCount(ssid) > 32) throw new ArgumentException("Wi-Fi 名称须为 1–32 字节。");
        if (password.Length != 0 && (password.Length < 8 || System.Text.Encoding.UTF8.GetByteCount(password) > 63)) throw new ArgumentException("Wi-Fi 密码须为 8–63 字节，开放网络留空。");
        await RequestAsync(HttpMethod.Post, "wifi", new() { ["ssid"] = ssid, ["password"] = password }, null, token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        bool needsRelease = _jobs.CurrentCount == 0 || _keyboardSession is not null || _padSession is not null || _leaseFailure is not null;
        _disposed = true;
        _lifetime.Cancel();
        try { await _heartbeat.ConfigureAwait(false); } catch (OperationCanceledException) { }
        if (needsRelease) await BestEffortCancelAsync().ConfigureAwait(false);
        _lifetime.Dispose(); _reports.Dispose(); _jobs.Dispose();
        if (_ownsClient) _http.Dispose();
    }

    public static (ushort Buttons, byte Dpad) ParsePad(string names)
    {
        ushort mask = 0; bool up = false, down = false, left = false, right = false;
        var buttons = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase) { ["Y"] = 1, ["B"] = 2, ["A"] = 4, ["X"] = 8, ["L"] = 16, ["R"] = 32, ["ZL"] = 64, ["ZR"] = 128, ["MINUS"] = 256, ["-"] = 256, ["PLUS"] = 512, ["L3"] = 1024, ["R3"] = 2048, ["HOME"] = 4096, ["CAPTURE"] = 8192 };
        foreach (string name in names.Split(['+', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            switch (name.ToUpperInvariant())
            {
                case "UP": up = true; break; case "DOWN": down = true; break; case "LEFT": left = true; break; case "RIGHT": right = true; break;
                default: if (!buttons.TryGetValue(name, out var bit)) throw new ArgumentException("未知手柄按键：" + name); mask |= bit; break;
            }
        int x = (right ? 1 : 0) - (left ? 1 : 0), y = (down ? 1 : 0) - (up ? 1 : 0);
        byte dpad = (x, y) switch { (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7, _ => 15 };
        return (mask, dpad);
    }

    public static (byte Modifiers, byte[] Keys) ParseKeyboard(string names)
    {
        byte mod = 0; var keys = new List<byte>();
        var special = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase) { ["ENTER"] = 0x28, ["RETURN"] = 0x28, ["ESC"] = 0x29, ["ESCAPE"] = 0x29, ["BACKSPACE"] = 0x2a, ["TAB"] = 0x2b, ["SPACE"] = 0x2c, ["CAPSLOCK"] = 0x39, ["PRINTSCREEN"] = 0x46, ["INSERT"] = 0x49, ["HOME"] = 0x4a, ["PAGEUP"] = 0x4b, ["DELETE"] = 0x4c, ["END"] = 0x4d, ["PAGEDOWN"] = 0x4e, ["RIGHT"] = 0x4f, ["LEFT"] = 0x50, ["DOWN"] = 0x51, ["UP"] = 0x52 };
        foreach (string raw in names.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = raw.ToUpperInvariant();
            byte modifier = name switch { "CTRL" or "CONTROL" or "LCTRL" => 1, "SHIFT" or "LSHIFT" => 2, "ALT" or "LALT" => 4, "WIN" or "LWIN" => 8, "RCTRL" => 16, "RSHIFT" => 32, "RALT" => 64, "RWIN" => 128, _ => 0 };
            if (modifier != 0) { mod |= modifier; continue; }
            byte key;
            if (name.Length == 1 && name[0] is >= 'A' and <= 'Z') key = (byte)(name[0] - 'A' + 4);
            else if (name.Length == 1 && name[0] is >= '1' and <= '9') key = (byte)(name[0] - '1' + 0x1e);
            else if (name == "0") key = 0x27;
            else if (name.StartsWith('F') && int.TryParse(name.AsSpan(1), out int f) && f is >= 1 and <= 12) key = (byte)(0x3a + f - 1);
            else if (!special.TryGetValue(name, out key)) throw new ArgumentException("未知键盘按键：" + raw);
            if (!keys.Contains(key)) keys.Add(key);
        }
        if (keys.Count > 6) throw new ArgumentException("最多同时按 6 个普通键。");
        return (mod, keys.ToArray());
    }
}
