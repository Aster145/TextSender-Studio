using System.Diagnostics;
using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using EasyCon.Script;
using EasyCon.Script.Bytecode;
using EasyCon.Script.Symbols;
using EasyCon.Script.Syntax;
using EasyScript;

namespace TextSenderStudio.Core.Scripting;

/// <summary>Optional full controller state used by EasyCon DOWN/UP and stick instructions.</summary>
public interface IStatefulScriptActions
{
    Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token);
}

/// <summary>Optional configured notification services for EasyCon ALERT.</summary>
public interface IScriptNotificationActions
{
    Task AlertAsync(string content, CancellationToken token);
}

/// <summary>Direct full-URL HTTP requests from the script, independent of a configured device.</summary>
public interface IScriptHttpActions
{
    Task RequestHttpAsync(string method, string url, string body, string contentType, CancellationToken token);
}

public sealed record ScriptDiagnostic(int Line, string Message, bool IsError)
{
    public override string ToString() => Line > 0 ? $"第 {Line} 行：{Message}" : Message;
}

public sealed class StudioScriptException(string message, int line = 0, Exception? inner = null)
    : Exception(line > 0 ? $"第 {line} 行：{message}" : message, inner)
{
    public int Line { get; } = line;
}

/// <summary>EasyCon's actual compiler/ECX VM, with Studio device and action adapters.</summary>
public sealed class StudioScriptEngine
{
    private const string Library = "textsender";
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public IReadOnlyList<ScriptDiagnostic> Validate(string source, IEnumerable<string>? functionNames = null)
    {
        try
        {
            return Compile(source, functionNames ?? []).Diagnostics.Select(d =>
                new ScriptDiagnostic(d.Location.StartLine + 1, d.Message, d.IsError)).ToArray();
        }
        catch (StudioScriptException ex) { return [new(ex.Line, ex.Message, true)]; }
    }

    public Task RunAsync(string source, IScriptActions actions, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actions);
        // Compilation and execution both stay off the UI thread. No disk cache or external module loading.
        return Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested();
            var names = actions.FunctionNames.ToArray();
            var compiled = Compile(source, names);
            var errors = compiled.Diagnostics.Where(d => d.IsError).ToArray();
            if (errors.Length != 0)
                throw new StudioScriptException(string.Join(Environment.NewLine,
                    errors.Select(d => $"第 {d.Location.StartLine + 1} 行：{d.Message}")));
            if (compiled.Image is not { } image) throw new StudioScriptException("编译器未生成可执行脚本。");

            var pad = new PadState(actions, token);
            var stopwatch = Stopwatch.StartNew();
            var host = new EcxHost
            {
                Caps = EcsCaps.AllDesktop,
                AppDir = AppContext.BaseDirectory,
                WaitMs = ms => Wait(ms, token),
                TimeMs = () => (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds),
                Rand = max => max > 0 ? Random.Shared.Next(max) : throw new StudioScriptException("RAND 的上限须大于 0。"),
                Key = pad.Tap,
                KeyState = pad.Hold,
                StickSet = pad.SetStick,
                StickClick = pad.TapStick,
                ImgLabel = name => Match(actions, name, token),
                Print = (text, _) => actions.Log(text),
                Alert = text =>
                {
                    token.ThrowIfCancellationRequested();
                    if (actions is IScriptNotificationActions notifications)
                        notifications.AlertAsync(text, token).GetAwaiter().GetResult();
                    else actions.Log(text);
                },
                ReadLine = () => throw new StudioScriptException("本版未接入交互式标准输入。"),
                Amiibo = _ => throw new StudioScriptException("当前设备不支持 AMIIBO 指令。"),
                Beep = (frequency, duration) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (frequency is < 37 or > 32767 || duration < 0)
                        throw new StudioScriptException("BEEP 频率须为 37–32767，时长不能为负数。");
                    Console.Beep(frequency, duration);
                }
            };
            host.Native = (name, args, context) =>
            {
                token.ThrowIfCancellationRequested();
                if (name == Library + "!SEND_TEXT")
                    actions.TypeAsync(context.Str(args[0]), token).GetAwaiter().GetResult();
                else if (name == Library + "!KEYBOARD")
                    actions.KeyAsync(context.Str(args[0]), args[1].AsInt(), token).GetAwaiter().GetResult();
                else if (name is Library + "!HTTP_GET" or Library + "!HTTP_POST" or Library + "!HTTP_REQUEST")
                {
                    if (actions is not IScriptHttpActions http)
                        throw new StudioScriptException("当前宿主未接入 HTTP 请求。");
                    if (name == Library + "!HTTP_GET")
                        http.RequestHttpAsync("GET", context.Str(args[0]), "", "application/json", token).GetAwaiter().GetResult();
                    else if (name == Library + "!HTTP_POST")
                        http.RequestHttpAsync("POST", context.Str(args[0]), context.Str(args[1]), "application/json", token).GetAwaiter().GetResult();
                    else
                        http.RequestHttpAsync(context.Str(args[0]), context.Str(args[1]), context.Str(args[2]), context.Str(args[3]), token).GetAwaiter().GetResult();
                }
                else if (name.StartsWith(Library + "!", StringComparison.Ordinal)
                    && names.Contains(name[(Library.Length + 1)..], StringComparer.Ordinal))
                    actions.CallAsync(name[(Library.Length + 1)..], token).GetAwaiter().GetResult();
                else throw new StudioScriptException($"当前宿主尚未实现：{name}。");
                return TaggedValue.Void;
            };
            try
            {
                var result = EcxInterpreter.Run(image, host, out int function, out int pc, token);
                if (result == EcxInterpreter.CANCELLED) throw new OperationCanceledException(token);
                if (result != EcxInterpreter.OK)
                {
                    int line = function >= 0 && function < image.Functions.Count ? image.Functions[function].LineAt(pc) : 0;
                    throw new StudioScriptException($"脚本运行错误 {result}（指令 {pc}）。", line);
                }
            }
            finally
            {
                if (pad.UsedState && actions is IStatefulScriptActions stateful)
                {
                    using var releaseTimeout = new CancellationTokenSource(2000);
                    try { await stateful.SetPadStateAsync("", 128, 128, 128, 128, releaseTimeout.Token).ConfigureAwait(false); }
                    catch (Exception ex) { actions.Log("释放手柄失败：" + ex.Message); }
                }
            }
        }, token);
    }

    private static CompileResult Compile(string source, IEnumerable<string> functionNames)
    {
        var appended = new StringBuilder(source).AppendLine().AppendLine();
        var seen = new HashSet<string>(StringComparer.Ordinal) { "SEND_TEXT", "KEYBOARD", "HTTP_GET", "HTTP_POST", "HTTP_REQUEST" };
        appended.AppendLine("EXTERN FUNC SEND_TEXT($text: STRING) FROM \"textsender\"");
        appended.AppendLine("EXTERN FUNC KEYBOARD($keys: STRING, $ms: INT) FROM \"textsender\"");
        appended.AppendLine("EXTERN FUNC HTTP_GET($url: STRING) FROM \"textsender\"");
        appended.AppendLine("EXTERN FUNC HTTP_POST($url: STRING, $body: STRING) FROM \"textsender\"");
        appended.AppendLine("EXTERN FUNC HTTP_REQUEST($method: STRING, $url: STRING, $body: STRING, $contentType: STRING) FROM \"textsender\"");
        foreach (var name in functionNames)
        {
            if (!Identifier.IsMatch(name) || !seen.Add(name))
                throw new StudioScriptException($"函数名无效或重复：{name}。");
            appended.AppendLine($"EXTERN FUNC {name}() FROM \"{Library}\"");
        }
        var labels = SyntaxTree.ParseTokens(source).Where(t => t.Type == TokenType.EX_VAR)
            .Select(t => t.Value[1..]).ToImmutableHashSet(StringComparer.Ordinal);
        return Compilation.CompileSource(appended.ToString(), new CompileOptions
            { UseDiskCache = false, UseProcessCache = false, ExtVars = labels });
    }

    private static int Match(IScriptActions actions, string name, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        double score = actions.MatchAsync(name, token).GetAwaiter().GetResult();
        if (!double.IsFinite(score)) throw new StudioScriptException($"标签 {name} 返回了无效匹配结果。");
        return (int)Math.Round(Math.Clamp(score, 0, 1) * 100, MidpointRounding.AwayFromZero);
    }

    private static void Wait(int milliseconds, CancellationToken token)
    {
        if (milliseconds < 0) throw new StudioScriptException("等待时长不能为负数。");
        token.ThrowIfCancellationRequested();
        if (milliseconds != 0) Task.Delay(milliseconds, token).GetAwaiter().GetResult();
    }

    private sealed class PadState(IScriptActions actions, CancellationToken token)
    {
        private readonly HashSet<string> _held = new(StringComparer.Ordinal);
        private byte _lx = 128, _ly = 128, _rx = 128, _ry = 128;
        public bool UsedState { get; private set; }

        private static string KeyName(int key) => ((GamePadKey)key) switch
        {
            GamePadKey.TOP => "UP", GamePadKey.TOP_LEFT => "UP+LEFT", GamePadKey.TOP_RIGHT => "UP+RIGHT",
            GamePadKey.DOWN_LEFT => "DOWN+LEFT", GamePadKey.DOWN_RIGHT => "DOWN+RIGHT",
            GamePadKey.LCLICK => "L3", GamePadKey.RCLICK => "R3", GamePadKey.None => "",
            GamePadKey.LS or GamePadKey.RS => throw new StudioScriptException("摇杆指令需要方向或坐标。"),
            var k when Enum.IsDefined(k) => k.ToString(),
            _ => throw new StudioScriptException($"无法识别的手柄按键：{key}。")
        };

        public void Tap(int key, int duration)
        {
            token.ThrowIfCancellationRequested();
            if (duration < 0) throw new StudioScriptException("按键时长不能为负数。");
            string name = KeyName(key);
            if (!UsedState) { actions.PadAsync(name, duration, token).GetAwaiter().GetResult(); return; }
            var previous = _held.ToArray();
            foreach (var button in name.Split('+', StringSplitOptions.RemoveEmptyEntries)) _held.Add(button);
            Send(); Wait(duration, token); _held.Clear(); _held.UnionWith(previous); Send();
        }

        public void Hold(int key, int down)
        {
            foreach (var button in KeyName(key).Split('+', StringSplitOptions.RemoveEmptyEntries))
                if (down != 0) _held.Add(button); else _held.Remove(button);
            Send();
        }

        public void SetStick(int stick, int x, int y)
        {
            if (x is < 0 or > 255 || y is < 0 or > 255) throw new StudioScriptException("摇杆坐标须为 0–255。");
            if (stick == 1) { _rx = (byte)x; _ry = (byte)y; }
            else { _lx = (byte)x; _ly = (byte)y; }
            Send();
        }

        public void TapStick(int stick, int x, int y, int duration)
        {
            if (duration < 0) throw new StudioScriptException("摇杆时长不能为负数。");
            SetStick(stick, x, y); Wait(duration, token); SetStick(stick, 128, 128);
        }

        private void Send()
        {
            token.ThrowIfCancellationRequested();
            if (actions is not IStatefulScriptActions stateful)
                throw new StudioScriptException("当前输入连接不支持按住/释放与摇杆状态指令。");
            UsedState = true;
            stateful.SetPadStateAsync(string.Join('+', _held.Order()), _lx, _ly, _rx, _ry, token).GetAwaiter().GetResult();
        }
    }
}
