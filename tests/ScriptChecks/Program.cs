using System.Diagnostics;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

var engine = new StudioScriptEngine();
int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description); checks++;
}
async Task<FakeActions> Run(string source, params string[] functions)
{
    var fake = new FakeActions(functions);
    await engine.RunAsync(source, fake, CancellationToken.None);
    return fake;
}

Check(!engine.Validate("WAIT 0\nA 100").Any(d => d.IsError), "native EasyCon syntax compiles");
var syntaxError = engine.Validate("IF 1 >\nENDIF");
Check(syntaxError.Any(d => d.IsError && d.Line == 1), "diagnostics include original source line");
Check(engine.Validate("CALL missing").Any(d => d.IsError), "unknown CALL rejected before execution");
Check(engine.Validate("WAIT 0", ["bad-name"]).Any(d => d.IsError), "invalid action name rejected");
Check(engine.Validate("WAIT 0", ["same", "same"]).Any(d => d.IsError), "duplicate action binding rejected");
Check(engine.Validate("WAIT 0", ["启动Tools", "启动TOOLS"]).Any(d => d.IsError), "mixed Chinese action duplicates rejected case-insensitively");
foreach (var name in new[] { "1开机", "开 机", "开-机", "开机\nWAIT", "开机()", "开机。", "开机😀", "" })
    Check(engine.Validate("WAIT 0", [name]).Any(d => d.IsError), "invalid Chinese action name rejected: " + name.Replace('\n', ' '));
foreach (var name in new[] { "WAIT", "print", "Call", "IF", "down", "int", "SEND_TEXT", "http_get", "__CAPTURE__" })
    Check(engine.Validate("WAIT 0", [name]).Any(d => d.IsError), "reserved action name rejected: " + name);

var chineseNames = new[] { "发送短语", "启动_工具2", "HTTP短按", "播放提醒", "_中文入口" };
var chineseCalls = await Run(string.Join('\n', chineseNames.Select(name => "CALL " + name)), chineseNames);
Check(chineseCalls.Calls.SequenceEqual(chineseNames), "Chinese phrase shortcut HTTP and sound wrappers execute without name translation");
var chineseFunction = await Run("FUNC 计算总数($数值):INT\nRETURN $数值 + 1\nENDFUNC\n$x = 计算总数(4)\nIF $x == 5\nCALL 完成提醒\nENDIF", "完成提醒");
Check(chineseFunction.Calls.SequenceEqual(["完成提醒"]), "Chinese local function and argument names preserve return values");
var underscores = await Run("_上限 = 5\nFUNC _计算($输入):INT\nRETURN $输入 + _上限\nENDFUNC\nFUNC _完成()\nCALL _中文动作\nENDFUNC\n$x = _计算(2)\nIF $x == 7\nCALL _完成\nENDIF", "_中文动作");
Check(underscores.Calls.SequenceEqual(["_中文动作"]), "underscore function declarations and CALL preserve ordinary constants");

var flow = await Run("$n = 0\nFOR 3\n$n = $n + 1\nA 25\nNEXT\nIF $n == 3\nPRINT correct\nELSE\nPRINT wrong\nENDIF");
Check(flow.Pads.SequenceEqual(["A:25", "A:25", "A:25"]), "FOR and key durations");
Check(flow.Messages.Any(x => x.Contains("correct")) && !flow.Messages.Any(x => x.Contains("wrong")), "variables and IF branch");
var function = await Run("FUNC sum($a, $b):INT\nRETURN $a + $b\nENDFUNC\n$x = sum(2, 5)\nIF $x == 7\nB 1\nENDIF");
Check(function.Pads.SequenceEqual(["B:1"]), "typed function arguments and return values");
var call = await Run("CALL phrase_hello\nCALL robot_action\nCALL start_tools", "phrase_hello", "robot_action", "start_tools");
Check(call.Calls.SequenceEqual(["phrase_hello", "robot_action", "start_tools"]), "CALL resolves configured actions in order");
var text = await Run("SEND_TEXT \"Hello\"\nKEYBOARD \"CTRL+S\", 50");
Check(text.Texts.SequenceEqual(["Hello"]) && text.Keys.SequenceEqual(["CTRL+S:50"]), "text and keyboard extensions use EasyCon call syntax");
var match = await Run("$score = @示例标签\nIF $score >= 90\nA 1\nENDIF");
Check(match.Matches.SequenceEqual(["示例标签"]) && match.Pads.SequenceEqual(["A:1"]), "image label confidence maps 0..1 to 0..100");
var state = await Run("L DOWN\nR DOWN\nWAIT 0\nL UP\nLS UP\nLS RESET\nR UP");
Check(state.States.Any(x => x.StartsWith("L+R:")), "held keys combine");
Check(state.States.Any(x => x == "R:128,0,128,128"), "left stick direction maps coordinates");
Check(state.States.Last() == ":128,128,128,128", "normal completion releases all held input");

var watch = Stopwatch.StartNew();
await Run("WAIT 60");
Check(watch.ElapsedMilliseconds >= 50, "WAIT uses milliseconds");
using (var cts = new CancellationTokenSource(80))
{
    var fake = new FakeActions([]);
    try { await engine.RunAsync("HOME DOWN\nWAIT 10000", fake, cts.Token); throw new Exception("did not cancel"); }
    catch (OperationCanceledException) { Check(fake.States.Last() == ":128,128,128,128", "cancellation releases held keys"); }
}
using (var cts = new CancellationTokenSource(80))
{
    try { await engine.RunAsync("FOR\n$x = 1\nNEXT", new FakeActions([]), cts.Token); throw new Exception("did not cancel"); }
    catch (OperationCanceledException) { Check(true, "compute loop is cancellable"); }
}
try { await Run("WAIT -1"); throw new Exception("accepted negative WAIT"); }
catch (StudioScriptException) { Check(true, "negative durations rejected"); }
try { await Run("AMIIBO 1"); throw new Exception("ignored unsupported input"); }
catch (StudioScriptException) { Check(true, "unsupported hardware feature reports an error"); }
try { await Run("EXTERN FUNC hidden() FROM \"user32.dll\"\nCALL hidden"); throw new Exception("loaded external DLL"); }
catch (StudioScriptException) { Check(true, "external host function is not silently loaded"); }
await ExecutionTrackingChecks.Run(engine, Check);
await PadStateChecks.Run(engine, Check);
await ScriptOutputChecks.Run(engine, Check);
Console.WriteLine($"Script checks passed: {checks}");

sealed class FakeActions(string[] functionNames) : IScriptActions, IStatefulScriptActions
{
    public IReadOnlyList<string> FunctionNames => functionNames;
    public List<string> Messages { get; } = [];
    public List<string> Pads { get; } = [];
    public List<string> Keys { get; } = [];
    public List<string> Texts { get; } = [];
    public List<string> Matches { get; } = [];
    public List<string> Calls { get; } = [];
    public List<string> States { get; } = [];
    public void Log(string text) => Messages.Add(text);
    public Task PadAsync(string buttons, int durationMs, CancellationToken token) { token.ThrowIfCancellationRequested(); Pads.Add($"{buttons}:{durationMs}"); return Task.CompletedTask; }
    public Task KeyAsync(string keys, int durationMs, CancellationToken token) { token.ThrowIfCancellationRequested(); Keys.Add($"{keys}:{durationMs}"); return Task.CompletedTask; }
    public Task TypeAsync(string text, CancellationToken token) { token.ThrowIfCancellationRequested(); Texts.Add(text); return Task.CompletedTask; }
    public Task<double> MatchAsync(string labelName, CancellationToken token) { token.ThrowIfCancellationRequested(); Matches.Add(labelName); return Task.FromResult(0.96); }
    public Task CallAsync(string name, CancellationToken token) { token.ThrowIfCancellationRequested(); Calls.Add(name); return Task.CompletedTask; }
    public Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token) { token.ThrowIfCancellationRequested(); States.Add($"{buttons}:{lx},{ly},{rx},{ry}"); return Task.CompletedTask; }
}
