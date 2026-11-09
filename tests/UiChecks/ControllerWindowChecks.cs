using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

internal static class ControllerWindowChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(MainWindow main, Action<bool, string> check, Action<Window, string> capture)
    {
        var type = typeof(MainWindow).Assembly.GetType("TextSenderStudio.ControllerWindow")!;
        var bridge = new RecordingBridge();
        var source = new DirectControlSettings();
        var saved = new List<DirectControlSettings>(); var recordings = new List<string>();
        var window = (Window)Activator.CreateInstance(type, main, bridge, source,
            new Action<DirectControlSettings>(saved.Add), new Action<string>(recordings.Add), false, false)!;
        window.Left = -20000; window.Top = -20000; window.WindowStartupLocation = WindowStartupLocation.Manual; window.ShowActivated = false;
        T Field<T>(string name) => (T)type.GetField(name, Hidden)!.GetValue(window)!;
        object? Call(string name, params object[] arguments) => type.GetMethod(name, Hidden)!.Invoke(window, arguments);
        void Start() { Await((Task)Call("StartAsync")!); Until(() => bridge.States.Count != 0); }
        void Key(int code, bool down) => Call("OnKey", down ? 0x100 : 0x101, new IntPtr(code), IntPtr.Zero);
        void Screen(string name, bool down) => Call("SetScreenTarget", name, down);
        bool Last(Func<PadState, bool> predicate) => bridge.States.TryPeek(out _) && predicate(bridge.States.Last());
        try
        {
            window.Show(); Pump();
            var targetButtons = Descendants<Button>(window).Where(button => button.Tag is string).ToDictionary(button => (string)button.Tag);
            check(targetButtons.Keys.Order().SequenceEqual(ControllerCatalog.Targets.Select(target => target.Name).Order()),
                "controller window exposes every EasyCon button, eight-way Dpad and both stick direction/reset controls");
            check(!Field<bool>("_running") && bridge.States.IsEmpty && !HookActive(Field<object>("_hook")),
                "controller opens paused with no device reports or native keyboard hook");
            Start();
            check(Field<bool>("_running") && targetButtons.Values.All(button => button.IsEnabled) && !HookActive(Field<object>("_hook")),
                "mocked controller starts with screen controls enabled and native hook disabled");
            Call("StartRecording");
            Key(76, true); Until(() => Last(state => state.Buttons == 4));
            Key(75, true); Until(() => Last(state => state.Buttons == 6));
            check(Last(state => state.Buttons == 6), "keyboard mapping preserves simultaneous A and B presses");
            Key(76, false); Until(() => Last(state => state.Buttons == 2));
            check(Last(state => state.Buttons == 2), "releasing one mapped key keeps the other held");
            Key(75, false); Until(() => Last(state => state == PadState.Neutral));
            Screen("LS_UPRIGHT", true); Until(() => Last(state => state.LX == 255 && state.LY == 0));
            Screen("RCLICK", true); Until(() => Last(state => state.Buttons == 2048 && state.LX == 255));
            check(Last(state => state.Buttons == 2048 && state.LX == 255 && state.LY == 0),
                "screen stick direction and stick click combine into one held state");
            Screen("RCLICK", false); Screen("LS_RESET", true);
            Until(() => Last(state => state.LX == 128 && state.LY == 128 && state.Buttons == 0));
            check(Last(state => state.LX == 128 && state.LY == 128), "screen stick reset returns its held direction to center");
            Screen("LS_UPRIGHT", false); Screen("LS_RESET", false);
            Screen("UPRIGHT", true); Until(() => Last(state => state.Hat == 1));
            Screen("UPRIGHT", false); Until(() => Last(state => state == PadState.Neutral));
            Call("StopRecording");
            check(recordings.Count == 1 && recordings[0].Contains("PAD_STATE") && recordings[0].Contains("WAIT ")
                && !new StudioScriptEngine().Validate(recordings[0]).Any(diagnostic => diagnostic.IsError),
                "recorded simultaneous inputs and analog state produce a compilable script without executing it");
            check(bridge.UnsupportedCalls == 0, "recording performs only live pad state and release operations");
            window.UpdateLayout();
            var viewport = Descendants<ScrollViewer>(window).Single(view => view.Content is StackPanel);
            bool AllTargetsVisible() => targetButtons.Values.All(button =>
            {
                var point = button.TranslatePoint(new Point(), viewport);
                return point.Y >= 0 && point.Y + button.ActualHeight <= viewport.ViewportHeight;
            });
            check(AllTargetsVisible(), "all controller targets fit the default viewport without scrolling");
            capture(window, "controller-900.png");
            window.Width = 760; Pump();
            check(targetButtons.Values.All(button =>
            {
                var location = button.TranslatePoint(new Point(), window);
                return location.X >= 0 && location.X + button.ActualWidth <= window.ActualWidth;
            }), "complete controller layout fits minimum window width");
            check(AllTargetsVisible(), "all controller targets stay visible at minimum window width");
            capture(window, "controller-760.png");

            Call("StartRecording"); Screen("A", true); Until(() => Last(state => state.Buttons == 4));
            int releases = bridge.Releases;
            typeof(Window).GetMethod("OnDeactivated", Hidden)!.Invoke(window, [EventArgs.Empty]);
            Until(() => !Field<bool>("_running") && bridge.Releases > releases && recordings.Count == 2);
            check(Last(state => state == PadState.Neutral) && !Field<ControllerRecorder>("_recorder").IsRecording
                && targetButtons.Values.All(button => !button.IsEnabled),
                "focus loss releases all inputs, pauses controls and delivers the recorded script");
            int pausedReports = bridge.States.Count; Key(76, true); Screen("A", true); Pump();
            check(bridge.States.Count == pausedReports, "paused window ignores further keyboard and screen inputs");

            Exception? mappingError = null;
            var mappingAutomation = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            mappingAutomation.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.Cast<Window>().SingleOrDefault(candidate => candidate.Title == "键盘映射");
                if (dialog is null) return;
                mappingAutomation.Stop();
                try
                {
                    var grid = Descendants<DataGrid>(dialog).Single();
                    var choices = (Array)dialog.GetType().GetMethod("KeyChoices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
                    var codes = choices.Cast<object>().Select(choice => (int)choice.GetType().GetProperty("Code")!.GetValue(choice)!).ToArray();
                    check(!codes.Intersect(new[] { 16, 17, 18 }).Any() && new[] { 160, 161, 162, 163, 164, 165 }.All(codes.Contains),
                        "mapping choices use side-specific modifiers matching captured virtual-key codes");
                    var row = grid.Items.Cast<object>().Single(item => item.GetType().GetProperty("Target")!.GetValue(item)?.ToString() == "A");
                    row.GetType().GetProperty("KeyCode")!.SetValue(row, 85);
                    check(!HookActive(Field<object>("_hook")) && !Field<bool>("_running"), "mapping editor opens with keyboard capture and controller paused");
                    capture(dialog, "controller-key-mapping.png");
                    Descendants<Button>(dialog).Single(button => button.Content?.ToString() == "保存映射")
                        .RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                }
                catch (Exception error)
                {
                    mappingError = error;
                    dialog.Close();
                }
            };
            mappingAutomation.Start();
            Await((Task)Call("EditMappingAsync")!);
            if (mappingError is not null) throw mappingError;
            check(saved.Last().KeyBindings["A"] == 85 && source.KeyBindings["A"] == 76,
                "keyboard mapping saves through callback while preserving the input settings object");
            Field<ComboBox>("_slot").SelectedIndex = 2;
            Until(() => saved.Last().GamepadIndex == 2);
            Field<CheckBox>("_gamepad").IsChecked = true;
            Until(() => saved.Last().GamepadEnabled);
            check(saved.Last().KeyBindings["A"] == 85 && saved.Last().GamepadIndex == 2,
                "source and slot changes retain the edited key mapping");
            Start(); Key(85, true); Until(() => Last(state => state.Buttons == 4));
            check(Field<TextBlock>("_gamepadStatus").Text.Contains("未读取实体手柄") && !HookActive(Field<object>("_hook")),
                "headless physical-source test never polls XInput or installs a keyboard hook");
            Key(85, false); Await((Task)Call("PauseAsync", "测试暂停")!);
        }
        finally
        {
            window.Close(); Until(() => !window.IsVisible);
        }
        check(!bridge.Disposed && bridge.UnsupportedCalls == 0, "controller cleanup leaves the shared bridge owned by the main window");
    }

    private static bool HookActive(object hook) => (bool)hook.GetType().GetProperty("IsActive")!.GetValue(hook)!;
    private static void Await(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 4000) Pump();
        if (!condition()) throw new TimeoutException("Controller UI check timed out.");
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private sealed class RecordingBridge : IInputBridge
    {
        public readonly ConcurrentQueue<PadState> States = new();
        public int Releases, UnsupportedCalls;
        public bool Disposed;
        public Task SetPadStateAsync(string buttons, byte lx, byte ly, byte rx, byte ry, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var parsed = InputBridge.ParsePad(buttons);
            States.Enqueue(new(parsed.Buttons, parsed.Dpad, lx, ly, rx, ry)); return Task.CompletedTask;
        }
        public Task ReleaseAllAsync(CancellationToken token) { Interlocked.Increment(ref Releases); States.Enqueue(PadState.Neutral); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        private Task Unsupported() { Interlocked.Increment(ref UnsupportedCalls); throw new InvalidOperationException("Unexpected device action in controller UI checks."); }
        public Task<string> StatusAsync(CancellationToken token) => throw new InvalidOperationException("No device status calls in controller UI checks.");
        public Task TypeTextAsync(string text, int characterDelayMs, int lineDelayMs, bool clearAutoIndent, CancellationToken token) => Unsupported();
        public Task TapKeyAsync(string keys, int durationMs, CancellationToken token) => Unsupported();
        public Task PressPadAsync(string buttons, int durationMs, CancellationToken token) => Unsupported();
        public Task SendKeyboardReportAsync(byte modifiers, byte[] keys, CancellationToken token) => Unsupported();
        public Task ConfigureWifiAsync(string ssid, string password, CancellationToken token) => Unsupported();
    }
}
