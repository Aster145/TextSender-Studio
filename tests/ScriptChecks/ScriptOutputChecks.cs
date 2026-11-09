using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

internal static class ScriptOutputChecks
{
    public static async Task Run(StudioScriptEngine engine, Action<bool, string> check)
    {
        var outputs = new List<ScriptOutput>();
        var events = new List<string>();
        var notified = new NotifiedActions(events);
        await engine.RunAsync("PRINT first\nWAIT 0\nA 0\nCALL sample\nALERT notice\nPRINT last", notified, CancellationToken.None,
            output: value => { outputs.Add(value); events.Add("output:" + value.Kind + ":" + value.Text); });
        check(outputs.SequenceEqual([new(ScriptOutputKind.Print, "first"), new(ScriptOutputKind.Alert, "notice"), new(ScriptOutputKind.Print, "last")]),
            "typed output includes only PRINT and ALERT in source order");
        check(notified.Logs.SequenceEqual(["first", "action diagnostic", "last"]), "typed output preserves existing PRINT and action logging");
        check(notified.Notifications.SequenceEqual(["notice"]) && events.IndexOf("output:Alert:notice") < events.IndexOf("notify:notice"),
            "typed ALERT output precedes and preserves configured notification delivery");
        check(events.IndexOf("output:Print:first") < events.IndexOf("log:first"), "typed PRINT output precedes ordinary host logging");

        outputs.Clear();
        var fallback = new LogActions([]);
        await engine.RunAsync("ALERT fallback", fallback, CancellationToken.None, output: outputs.Add);
        check(outputs.Single() == new ScriptOutput(ScriptOutputKind.Alert, "fallback") && fallback.Logs.SequenceEqual(["fallback"]),
            "ALERT without notification service still logs normally and emits typed output");
        var unchanged = new LogActions([]);
        await engine.RunAsync("PRINT original\nALERT alert", unchanged, CancellationToken.None);
        check(unchanged.Logs.SequenceEqual(["original", "alert"]), "omitting output callback preserves legacy logging behavior");

        outputs.Clear();
        try { await engine.RunAsync("IF 1 >\nENDIF", new LogActions([]), CancellationToken.None, output: outputs.Add); throw new Exception("Compile error expected."); }
        catch (StudioScriptException) { check(outputs.Count == 0, "compile diagnostics do not enter typed script output"); }
        try { await engine.RunAsync("WAIT -1", new LogActions([]), CancellationToken.None, output: outputs.Add); throw new Exception("Runtime error expected."); }
        catch (StudioScriptException) { check(outputs.Count == 0, "runtime diagnostics do not enter typed script output"); }
    }

    private class LogActions(List<string> events) : IScriptActions
    {
        protected List<string> Events { get; } = events;
        public List<string> Logs { get; } = [];
        public IReadOnlyList<string> FunctionNames => ["sample"];
        public void Log(string text) { Logs.Add(text); Events.Add("log:" + text); }
        public Task CallAsync(string functionName, CancellationToken token) { Log("action diagnostic"); return Task.CompletedTask; }
        public Task PadAsync(string buttons, int durationMs, CancellationToken token) => Task.CompletedTask;
        public Task KeyAsync(string keys, int durationMs, CancellationToken token) => throw new InvalidOperationException("Unexpected keyboard action.");
        public Task TypeAsync(string text, CancellationToken token) => throw new InvalidOperationException("Unexpected text action.");
        public Task<double> MatchAsync(string labelName, CancellationToken token) => throw new InvalidOperationException("Unexpected image action.");
    }

    private sealed class NotifiedActions(List<string> events) : LogActions(events), IScriptNotificationActions
    {
        public List<string> Notifications { get; } = [];
        public Task AlertAsync(string content, CancellationToken token) { Notifications.Add(content); Events.Add("notify:" + content); return Task.CompletedTask; }
    }
}
