using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace TextSenderStudio.Core;

public sealed class ProjectActions(AppSettings settings, StudioProject project, string projectPath, IInputBridge bridge, Func<string, CancellationToken, Task<double>> match, Action<string> log, Func<SoundAction, CancellationToken, Task>? playSound = null) : IScriptActions, Scripting.IStatefulScriptActions, Scripting.IScriptNotificationActions, Scripting.IScriptHttpActions
{
    public IReadOnlyList<string> FunctionNames => project.Phrases.Select(p => p.FunctionName).Concat(project.Shortcuts.Select(p => p.FunctionName)).Concat(project.HttpActions.Select(p => p.FunctionName)).Concat(project.Sounds.Select(p => p.FunctionName)).ToArray();
    public void Log(string text) => log(text);
    public async Task AlertAsync(string content, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); log(content);
        using var dispatcher = new PushNotificationDispatcher();
        foreach (var result in await dispatcher.SendAsync(settings.PushNotifications, content, token: token)) log(result);
    }
    public Task PadAsync(string buttons, int durationMs, CancellationToken token) => bridge.PressPadAsync(buttons, durationMs, token);
    public Task KeyAsync(string keys, int durationMs, CancellationToken token) => bridge.TapKeyAsync(keys, durationMs, token);
    public Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token) => bridge.SetPadStateAsync(buttons, lx, ly, rx, ry, token);
    public Task<double> MatchAsync(string labelName, CancellationToken token) => match(labelName, token);
    public Task RequestHttpAsync(string method, string url, string body, string contentType, CancellationToken token)
    {
        if (url.StartsWith('/')) throw new ArgumentException("脚本 HTTP 指令请填写完整 HTTP / HTTPS URL。");
        return HttpAsync(new HttpAction { Steps = [new() { Method = method, Path = url, Body = body, ContentType = contentType }] }, token);
    }
    public async Task TypeAsync(string text, CancellationToken token)
    {
        await Task.Delay(settings.SendDelayMs, token);
        await bridge.TypeTextAsync(text, settings.CharacterDelayMs, settings.LineDelayMs, settings.ClearAutoIndent, token);
    }
    public async Task CallAsync(string functionName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var phrase = project.Phrases.FirstOrDefault(p => p.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase));
        if (phrase != null) { await TypeAsync(phrase.Text + (settings.PhraseAutoEnter ? "\n" : ""), token); return; }
        var shortcut = project.Shortcuts.FirstOrDefault(p => p.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase));
        if (shortcut != null) { await LaunchAsync(shortcut, token); return; }
        var action = project.HttpActions.FirstOrDefault(p => p.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase));
        if (action != null) { await HttpAsync(action, token); return; }
        var sound = project.Sounds.FirstOrDefault(p => p.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase));
        if (sound != null)
        {
            if (playSound == null) throw new InvalidOperationException("当前宿主未接入声音播放。");
            await playSound(sound, token); log("声音提醒：" + sound.Name); return;
        }
        throw new InvalidOperationException("当前项目没有函数：" + functionName);
    }
    public async Task HttpAsync(HttpAction action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var targets = new List<Uri>();
        foreach (var step in action.Steps)
        {
            if (step.WaitAfterMs < 0 || step.WaitAfterMs > 600000) throw new ArgumentException("步骤等待范围为 0–600000 ms。");
            targets.Add(HttpRequestAddress.Resolve(step, settings));
            if (!new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" }.Contains(step.Method.ToUpperInvariant())) throw new ArgumentException("HTTP 方法无效。");
            if (HasHttpContent(step)) _ = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(step.ContentType);
            using var headerCheck = new HttpRequestMessage();
            foreach (var header in step.Headers)
                if (header.Value.Contains('\r') || header.Value.Contains('\n') || !headerCheck.Headers.TryAddWithoutValidation(header.Key, header.Value)) throw new ArgumentException("不支持的请求头：" + header.Key);
        }
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(settings.RequestTimeoutMs) };
        for (var index = 0; index < action.Steps.Count; index++)
        {
            var step = action.Steps[index];
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(new HttpMethod(step.Method.ToUpperInvariant()), targets[index]);
            if (HasHttpContent(step))
            {
                var contentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(step.ContentType);
                contentType.CharSet ??= "utf-8";
                request.Content = new StringContent(step.Body, Encoding.UTF8);
                request.Content.Headers.ContentType = contentType;
            }
            foreach (var header in step.Headers) if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value)) throw new ArgumentException("不支持的请求头：" + header.Key);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode(); log($"HTTP {step.Method} {step.Path} → {(int)response.StatusCode}");
            if (step.WaitAfterMs < 0) throw new ArgumentException("步骤等待不能小于 0。");
            await Task.Delay(step.WaitAfterMs, token);
        }
    }
    private static bool HasHttpContent(HttpStep step) => step.Body.Length > 0
        || step.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
        || step.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase)
        || step.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase);
    public async Task LaunchAsync(ScriptShortcut shortcut, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(shortcut.Path, projectPath);
        if (!File.Exists(path)) throw new FileNotFoundException("启动脚本不存在。", path);
        var directory = string.IsNullOrWhiteSpace(shortcut.WorkingDirectory) ? Path.GetDirectoryName(path)! : Path.GetFullPath(shortcut.WorkingDirectory, projectPath);
        var info = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = directory };
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".cmd": case ".bat":
                info.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                info.Arguments = "/d /s /c \"\"" + path + "\" " + shortcut.Arguments + "\""; break;
            case ".ps1":
                info.FileName = "powershell.exe";
                info.Arguments = "-NoProfile -File \"" + path + "\" " + shortcut.Arguments; break;
            case ".exe": info.FileName = path; info.Arguments = shortcut.Arguments; break;
            default: throw new ArgumentException("仅支持 .cmd、.bat、.ps1 和 .exe。");
        }
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new IOException("脚本未能启动。");
        log("已启动：" + shortcut.Name);
        if (!shortcut.WaitForExit) return;
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new IOException($"脚本退出码：{process.ExitCode}");
    }
}
