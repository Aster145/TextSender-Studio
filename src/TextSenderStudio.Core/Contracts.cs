namespace TextSenderStudio.Core;

public interface IInputBridge : IAsyncDisposable
{
    Task<string> StatusAsync(CancellationToken token);
    Task TypeTextAsync(string text, int characterDelayMs, int lineDelayMs, bool clearAutoIndent, CancellationToken token);
    Task TapKeyAsync(string keys, int durationMs, CancellationToken token);
    Task PressPadAsync(string buttons, int durationMs, CancellationToken token);
    Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token);
    Task SendKeyboardReportAsync(byte modifiers, byte[] keys, CancellationToken token);
    Task ReleaseAllAsync(CancellationToken token);
    Task ConfigureWifiAsync(string ssid, string password, CancellationToken token);
}

public interface IScriptActions
{
    void Log(string text);
    Task PadAsync(string buttons, int durationMs, CancellationToken token);
    Task KeyAsync(string keys, int durationMs, CancellationToken token);
    Task TypeAsync(string text, CancellationToken token);
    Task<double> MatchAsync(string labelName, CancellationToken token);
    Task CallAsync(string functionName, CancellationToken token);
    IReadOnlyList<string> FunctionNames { get; }
}
