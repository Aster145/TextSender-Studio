using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

internal static class ScriptLogChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(MainWindow main, Action<bool, string> check)
    {
        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        var toggle = (CheckBox)main.FindName("MainScriptEditToggle");
        var output = (TextBox)main.FindName("ScriptOutputBox");
        var diagnostics = (DiagnosticLogBuffer)typeof(MainWindow).GetField("_diagnosticLog", Hidden)!.GetValue(main)!;
        var runScript = typeof(MainWindow).GetMethod("RunScriptWithPositionAsync", Hidden)!;
        var runOperation = typeof(MainWindow).GetMethod("RunOperation", Hidden)!;
        var log = typeof(MainWindow).GetMethod("Log", Hidden)!;
        var stop = typeof(MainWindow).GetMethod("StopClick", Hidden)!;
        var sourceBefore = editor.Text; var outputBefore = output.Text; bool? editBefore = toggle.IsChecked;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new LogActions(message => log.Invoke(main, [message]), gate.Task);
        Task? running = null;
        Task RunSource(string source)
        {
            editor.Text = source;
            Func<CancellationToken, Task> work = token => (Task)runScript.Invoke(main, [source, fake, token])!;
            return (Task)runOperation.Invoke(main, [work, false])!;
        }
        string[] Tags() => output.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line[(line.IndexOf('[') + 1)..line.IndexOf(']')]).ToArray();
        try
        {
            toggle.IsChecked = true; output.Clear();
            log.Invoke(main, ["diagnostic-before-open"]);
            running = RunSource("PRINT visible-one\nCALL gate\nALERT visible-alert\nPRINT visible-two");
            Until(() => fake.GateEntered && output.Text.Contains("visible-one"));
            check(editor.IsReadOnly && !toggle.IsEnabled, "script logging run also locks main script editing");
            check(!output.Text.Contains("diagnostic-before-open") && !output.Text.Contains("action-diagnostic"), "main script panel excludes connection and action diagnostics");
            check(Descendants<Button>(main).Any(button => button.Content?.ToString() == "设置" && button.IsVisible && button.IsEnabled), "settings remains available while script is running");
            var store = new ProjectStore(App.DataRoot);
            string settingsBefore = JsonSerializer.Serialize(store.LoadSettings(), ProjectStore.Json);
            OpenSettings(main, dialog =>
            {
                var tabs = Descendants<TabControl>(dialog).Single();
                var general = tabs.Items.OfType<TabItem>().Single(tab => tab.Header?.ToString() == "常规设置");
                var logs = tabs.Items.OfType<TabItem>().Single(tab => tab.Header?.ToString() == "执行日志");
                var box = Descendants<TextBox>(dialog).Single(item => item.Name == "DiagnosticLogBox");
                check(tabs.SelectedItem == logs && !general.IsEnabled && !Descendants<Button>(dialog).Single(button => button.Content?.ToString() == "保存设置").IsEnabled,
                    "running settings opens live logs and disables configuration changes and save");
                check(box.Text.Contains("diagnostic-before-open") && box.Text.Contains("action-diagnostic"), "settings shows diagnostic history emitted before it opened");
                log.Invoke(main, ["diagnostic-while-open"]);
                check(box.Text.Contains("diagnostic-while-open") && box.IsReadOnly, "settings log updates live and remains read-only");
                Descendants<Button>(dialog).Single(button => button.Content?.ToString() == "关闭").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });
            check(JsonSerializer.Serialize(store.LoadSettings(), ProjectStore.Json) == settingsBefore, "viewing logs during execution does not save settings");
            gate.TrySetResult(); Until(() => running.IsCompleted); running.GetAwaiter().GetResult(); PumpFor(20);
            check(Tags().SequenceEqual(["开始", "PRINT", "ALERT", "PRINT", "结束"]) && output.Text.Contains("[结束] 已完成"),
                "main script output contains ordered lifecycle, PRINT and ALERT entries only");
            check(fake.Notifications.SequenceEqual(["visible-alert"]) && diagnostics.Text.Contains("visible-one") && diagnostics.Text.Contains("diagnostic-while-open"),
                "script output filtering preserves original host diagnostics and fake notification dispatch");

            output.Clear();
            running = RunSource("PRINT before-cancel\nWAIT 10000");
            Until(() => output.Text.Contains("before-cancel")); stop.Invoke(main, [main, new RoutedEventArgs()]);
            Until(() => running.IsCompleted); running.GetAwaiter().GetResult(); PumpFor(20);
            check(Tags().SequenceEqual(["开始", "PRINT", "结束"]) && output.Text.Contains("[结束] 已停止"), "cancelled script shows one stopped lifecycle entry without diagnostic noise");

            output.Clear(); running = RunSource("IF 1 >\nENDIF");
            Until(() => running.IsCompleted); running.GetAwaiter().GetResult(); PumpFor(20);
            check(Tags().SequenceEqual(["开始", "结束"]) && output.Text.Contains("[结束] 执行失败") && !output.Text.Contains("第 1 行"),
                "failed script reports end state while detailed compiler diagnostics stay out of main output");
            check(diagnostics.Text.Contains("第 1 行"), "compiler error details remain available in settings diagnostics");
            var bounded = new DiagnosticLogBuffer(); bounded.Append(new string('x', 26000));
            check(bounded.Text.Length <= 24000, "diagnostic history remains bounded");
        }
        finally
        {
            gate.TrySetResult();
            if (running is { IsCompleted: false }) { stop.Invoke(main, [main, new RoutedEventArgs()]); Until(() => running.IsCompleted); }
            PumpFor(20); editor.Text = sourceBefore; toggle.IsChecked = editBefore; output.Text = outputBefore;
        }
    }

    private static void OpenSettings(MainWindow main, Action<Window> inspect)
    {
        Exception? error = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().Single(window => window != main && window.Title == "设置");
            try { dialog.Left = -20000; dialog.Top = -20000; dialog.UpdateLayout(); inspect(dialog); }
            catch (Exception failure) { error = failure; dialog.Close(); }
        });
        typeof(MainWindow).GetMethod("SettingsClick", Hidden)!.Invoke(main, [main, new RoutedEventArgs()]);
        if (error != null) throw error;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T found) yield return found;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var result in Descendants<T>(child)) yield return result;
    }
    private static void Until(Func<bool> complete)
    {
        var clock = Stopwatch.StartNew();
        while (!complete() && clock.ElapsedMilliseconds < 5000) PumpFor(10);
        if (!complete()) throw new InvalidOperationException("Timed out waiting for script output UI.");
    }
    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    private sealed class LogActions(Action<string> log, Task gate) : IScriptActions, IScriptNotificationActions
    {
        private int _gateEntered;
        public bool GateEntered => Volatile.Read(ref _gateEntered) != 0;
        public List<string> Notifications { get; } = [];
        public IReadOnlyList<string> FunctionNames => ["gate"];
        public void Log(string text) => log(text);
        public async Task CallAsync(string functionName, CancellationToken token) { log("action-diagnostic"); Volatile.Write(ref _gateEntered, 1); await gate.WaitAsync(token); }
        public Task AlertAsync(string content, CancellationToken token) { Notifications.Add(content); return Task.CompletedTask; }
        public Task PadAsync(string buttons, int durationMs, CancellationToken token) => throw new InvalidOperationException("No hardware actions in log UI checks.");
        public Task KeyAsync(string keys, int durationMs, CancellationToken token) => throw new InvalidOperationException("No hardware actions in log UI checks.");
        public Task TypeAsync(string text, CancellationToken token) => throw new InvalidOperationException("No hardware actions in log UI checks.");
        public Task<double> MatchAsync(string labelName, CancellationToken token) => throw new InvalidOperationException("No capture in log UI checks.");
    }
}
