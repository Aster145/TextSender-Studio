using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

internal static class ExecutionTrackingChecks
{
    public static async Task Run(StudioScriptEngine engine, Action<bool, string> check)
    {
        var tracker = new ScriptExecutionTracker();
        check(!tracker.Enabled && !tracker.Running && tracker.CurrentLine == 0, "execution tracking defaults hidden and inactive");
        using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var running = engine.RunAsync("# first line\nWAIT 10000", new ProbeActions(), cancel.Token, tracker);
            await Until(() => tracker.Running);
            await Task.Delay(40);
            check(tracker.CurrentLine == 0, "disabled tracking exposes no visible source line");
            tracker.Enabled = true;
            await Until(() => tracker.CurrentLine == 2);
            check(tracker.CurrentLine == 2, "enabling during an existing WAIT reports its source line");
            tracker.Enabled = false;
            check(tracker.CurrentLine == 0, "runtime toggle hides execution position immediately");
            tracker.Enabled = true;
            check(tracker.CurrentLine == 2, "re-enabling during WAIT restores same executing line");
            cancel.Cancel();
            try { await running; throw new Exception("expected cancellation"); } catch (OperationCanceledException) { }
            check(!tracker.Running && tracker.CurrentLine == 0, "cancellation clears execution position");
        }

        var observed = new List<(string Kind, int Line)>();
        var actions = new ProbeActions { Observe = kind => observed.Add((kind, tracker.CurrentLine)) };
        await engine.RunAsync("FUNC work()\n    WAIT 0\n    CALL pause\nENDFUNC\nIF 1 == 1\n    CALL work\nENDIF\nCALL after", actions, CancellationToken.None, tracker);
        check(observed.SequenceEqual([("pause", 3), ("after", 8)]), "local functions and return map to original source lines");
        check(!tracker.Running && tracker.CurrentLine == 0, "normal completion clears execution position");
        observed.Clear();
        await engine.RunAsync("A 1\nKEYBOARD \"ENTER\", 1\nSEND_TEXT \"x\"\n$x = @标签\nHTTP_GET \"http://example.invalid/\"\nALERT done", actions, CancellationToken.None, tracker);
        check(observed.SequenceEqual([("pad", 1), ("key", 2), ("text", 3), ("image", 4), ("http", 5), ("alert", 6)]), "blocking host operations publish exact caller line before dispatch");
        observed.Clear();
        await engine.RunAsync("$x = 0\nFOR 2\n    IF $x == 0\n        CALL pause\n    ELSE\n        CALL after\n    ENDIF\n    $x = $x + 1\nNEXT", actions, CancellationToken.None, tracker);
        check(observed.SequenceEqual([("pause", 4), ("after", 6)]), "loop branches report the branch actually executed");
        try { await engine.RunAsync("IF 1 >\nENDIF", actions, CancellationToken.None, tracker); throw new Exception("accepted invalid syntax"); }
        catch (StudioScriptException) { check(!tracker.Running && tracker.CurrentLine == 0, "compile failure leaves no stale execution marker"); }
        actions.Observe = _ => throw new InvalidOperationException("mock host failure");
        try { await engine.RunAsync("CALL pause", actions, CancellationToken.None, tracker); throw new Exception("ignored host failure"); }
        catch (InvalidOperationException) { check(!tracker.Running && tracker.CurrentLine == 0, "host failure clears execution position"); }
        using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var running = engine.RunAsync("$x = 0\nFOR\n    $x = $x + 1\nNEXT", new ProbeActions(), cancel.Token, tracker);
            await Until(() => tracker.CurrentLine > 0);
            check(tracker.CurrentLine is >= 1 and <= 4, "pure compute loop publishes sampled source position");
            cancel.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            check(!tracker.Running && tracker.CurrentLine == 0, "compute-loop cancellation clears position");
        }
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            try { await engine.RunAsync("WAIT 0", new ProbeActions(), cancel.Token, tracker); } catch (OperationCanceledException) { }
            check(!tracker.Running && tracker.CurrentLine == 0, "pre-cancelled script leaves tracker inactive");
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(3);
        while (!condition()) { if (DateTime.UtcNow >= limit) throw new TimeoutException("execution probe did not advance"); await Task.Delay(5); }
    }
    private sealed class ProbeActions : IScriptActions, IScriptHttpActions, IScriptNotificationActions
    {
        public Action<string> Observe { get; set; } = _ => { };
        public IReadOnlyList<string> FunctionNames => ["pause", "after"];
        public void Log(string text) => Observe("print");
        private Task Record(string value, CancellationToken token) { token.ThrowIfCancellationRequested(); Observe(value); return Task.CompletedTask; }
        public Task PadAsync(string buttons, int durationMs, CancellationToken token) => Record("pad", token);
        public Task KeyAsync(string keys, int durationMs, CancellationToken token) => Record("key", token);
        public Task TypeAsync(string text, CancellationToken token) => Record("text", token);
        public Task CallAsync(string name, CancellationToken token) => Record(name, token);
        public Task<double> MatchAsync(string labelName, CancellationToken token) { token.ThrowIfCancellationRequested(); Observe("image"); return Task.FromResult(0.98); }
        public Task RequestHttpAsync(string method, string url, string body, string contentType, CancellationToken token) => Record("http", token);
        public Task AlertAsync(string content, CancellationToken token) => Record("alert", token);
    }
}
