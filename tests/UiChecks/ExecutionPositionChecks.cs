using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;

internal static class ExecutionPositionChecks
{
    public static void Run(MainWindow main, Action<bool, string> check, Action<Window, string> capture)
    {
        var toggle = (CheckBox)main.FindName("ShowExecutionPositionCheck");
        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        var editToggle = (CheckBox)main.FindName("MainScriptEditToggle");
        bool? wasEditing = editToggle.IsChecked; editToggle.IsChecked = true;
        var tabs = (TabControl)main.FindName("MainTabs");
        var status = (TextBlock)main.FindName("ExecutionPositionStatus");
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("_executionPositionTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var run = typeof(MainWindow).GetMethod("RunScriptWithPositionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var store = new ProjectStore(App.DataRoot);
        string original = editor.Text;
        bool wasEnabled = toggle.IsChecked == true;
        tabs.SelectedIndex = 0; main.UpdateLayout();
        using var cancel = new CancellationTokenSource();
        Task? running = null;
        try
        {
            check(!toggle.IsChecked.GetValueOrDefault() && !timer.IsEnabled, "execution position starts disabled without a UI timer");
            const string source = "# 执行位置测试，不连接设备\nWAIT 10000\nPRINT finished";
            editor.Text = source; editor.Select(0, 2);
            running = (Task)run.Invoke(main, [source, new QuietActions(), cancel.Token])!;
            PumpFor(200);
            check(editor.ExecutionLine == 0 && !timer.IsEnabled, "running with display off keeps marker and timer hidden");
            toggle.IsChecked = true;
            Until(() => editor.ExecutionLine == 2, 2000);
            check(editor.ExecutionLine == 2 && timer.IsEnabled, "enabling during WAIT highlights its existing source line");
            check(editor.SelectionStart == 0 && editor.SelectionLength == 2, "live highlight does not change selected text");
            check(store.LoadSettings().ShowExecutionPosition, "execution display preference persists in settings");
            editor.BringIntoView(); main.UpdateLayout(); PumpFor(20);
            capture(main, "execution-position.png");
            toggle.IsChecked = false;
            check(editor.ExecutionLine == 0 && !timer.IsEnabled && status.Visibility == Visibility.Collapsed, "switching off immediately clears highlight and stops UI polling");
            toggle.IsChecked = true; Until(() => editor.ExecutionLine == 2, 1000);
            editor.Text = source + "\n# 修改草稿";
            check(editor.ExecutionLine == 0 && status.Text.Contains("内容已修改"), "editing the running snapshot hides stale line mapping");
            editor.Text = source; Until(() => editor.ExecutionLine == 2, 1000);
            tabs.SelectedIndex = 1; PumpFor(120);
            check(editor.ExecutionLine == 0, "separate text tab hides the offscreen execution marker");
            tabs.SelectedIndex = 0; Until(() => editor.ExecutionLine == 2, 1000);
            cancel.Cancel(); Until(() => running.IsCompleted, 2000);
            try { running.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            check(editor.ExecutionLine == 0 && !timer.IsEnabled && status.Visibility == Visibility.Collapsed, "cancellation clears the marker and ends polling");

            editor.Text = "WAIT 150\nPRINT finished";
            running = (Task)run.Invoke(main, [editor.Text, new QuietActions(), CancellationToken.None])!;
            Until(() => running.IsCompleted, 2000); running.GetAwaiter().GetResult();
            check(editor.ExecutionLine == 0 && !timer.IsEnabled, "normal completion clears the marker and polling");
            editor.Text = "IF 1 >\nENDIF";
            running = (Task)run.Invoke(main, [editor.Text, new QuietActions(), CancellationToken.None])!;
            Until(() => running.IsCompleted, 2000);
            try { running.GetAwaiter().GetResult(); } catch (TextSenderStudio.Core.Scripting.StudioScriptException) { }
            check(running.IsFaulted && editor.ExecutionLine == 0 && !timer.IsEnabled, "compile failure cannot leave a stale execution marker");
        }
        finally
        {
            cancel.Cancel();
            if (running is { IsCompleted: false }) Until(() => running.IsCompleted, 2000);
            toggle.IsChecked = wasEnabled; tabs.SelectedIndex = 0; editor.Text = original;
            editToggle.IsChecked = wasEditing;
        }
    }

    private static void Until(Func<bool> condition, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < milliseconds) PumpFor(10);
        if (!condition()) throw new InvalidOperationException("Timed out waiting for execution position UI state.");
    }

    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        tick.Tick += (_, _) => { tick.Stop(); frame.Continue = false; };
        tick.Start(); Dispatcher.PushFrame(frame);
    }

    private sealed class QuietActions : IScriptActions
    {
        public IReadOnlyList<string> FunctionNames => [];
        public void Log(string text) { }
        public Task PadAsync(string buttons, int durationMs, CancellationToken token) => throw new InvalidOperationException("No device actions in UI checks.");
        public Task KeyAsync(string keys, int durationMs, CancellationToken token) => throw new InvalidOperationException("No device actions in UI checks.");
        public Task TypeAsync(string text, CancellationToken token) => throw new InvalidOperationException("No device actions in UI checks.");
        public Task<double> MatchAsync(string labelName, CancellationToken token) => throw new InvalidOperationException("No capture in UI checks.");
        public Task CallAsync(string functionName, CancellationToken token) => throw new InvalidOperationException("No external actions in UI checks.");
    }
}
