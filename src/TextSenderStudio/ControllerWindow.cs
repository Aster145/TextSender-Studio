using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal sealed class ControllerWindow : Window
{
    private readonly KeyboardCaptureHook _hook = new();
    private readonly DirectControlPump _pump;
    private readonly ControllerRecorder _recorder = new();
    private readonly DispatcherTimer _gamepadTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly HashSet<int> _keys = [];
    private readonly HashSet<string> _screenTargets = [];
    private readonly Dictionary<string, Button> _targetButtons = [];
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _confirmedGate = new();
    private readonly Action<DirectControlSettings> _saveSettings;
    private readonly Action<string> _recordedScript;
    private readonly bool _enableNativeInput;
    private DirectControlSettings _settings;
    private PadState _physical = PadState.Neutral, _lastConfirmed = PadState.Neutral;
    private readonly Button _start = new() { Content = "开始控制" };
    private readonly Button _pause = new() { Content = "暂停并释放", IsEnabled = false };
    private readonly Button _record = new() { Content = "开始录制", IsEnabled = false };
    private readonly CheckBox _keyboard = new() { Content = "键盘映射", Margin = new(0, 0, 20, 0) };
    private readonly CheckBox _gamepad = new() { Content = "实体手柄 · XInput", Margin = new(0, 0, 12, 0) };
    private readonly ComboBox _slot = new() { Width = 112, ItemsSource = new[] { "手柄 1", "手柄 2", "手柄 3", "手柄 4" } };
    private readonly CheckBox _swap = new() { Content = "按 Nintendo 位置映射 ABXY", Margin = new(0, 12, 0, 0) };
    private readonly TextBlock _status = new() { Text = "已暂停 · 点击“开始控制”", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _gamepadStatus = new() { Text = "XInput 未启用", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _axes = new() { Text = "左摇杆 · 回中    右摇杆 · 回中" };
    private readonly TextBlock _recordStatus = new() { Text = "录制会生成毫秒等待和手柄指令，结束后插入主脚本。", TextWrapping = TextWrapping.Wrap };
    private bool _running, _closing, _closeReady, _changingOptions, _startRecordingWhenActive;
    private int _generation, _pumpFaulted;
    private static readonly IReadOnlyDictionary<string, (Brush Background, Brush Foreground)> FaceColors =
        new Dictionary<string, (Brush, Brush)> {
            ["X"] = (ColorBrush("#DBEAFE"), ColorBrush("#1D4ED8")),
            ["Y"] = (ColorBrush("#FEF3C7"), ColorBrush("#92400E")),
            ["A"] = (ColorBrush("#DCFCE7"), ColorBrush("#166534")),
            ["B"] = (ColorBrush("#FEE2E2"), ColorBrush("#B91C1C")) };

    public ControllerWindow(Window owner, IInputBridge bridge, DirectControlSettings settings,
        Action<DirectControlSettings> saveSettings, Action<string> recordedScript,
        bool startRecording = false, bool enableNativeInput = true)
    {
        Owner = owner; Title = "手柄控制与录制 — TextSender Studio";
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Width = 900; Height = 820; MinWidth = 760; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = owner.Topmost;
        _settings = ProjectStore.Clone(settings); _saveSettings = saveSettings; _recordedScript = recordedScript;
        _enableNativeInput = enableNativeInput; _startRecordingWhenActive = startRecording;
        _pump = new DirectControlPump(bridge);
        BuildInterface();
        _changingOptions = true;
        _keyboard.IsChecked = _settings.KeyboardEnabled; _gamepad.IsChecked = _settings.GamepadEnabled;
        _slot.SelectedIndex = Math.Clamp(_settings.GamepadIndex, 0, 3); _swap.IsChecked = _settings.SwapFaceButtons;
        _changingOptions = false;
        _start.Click += async (_, _) => await StartAsync();
        _pause.Click += async (_, _) => await PauseAsync("已暂停，所有按键和摇杆已释放。");
        _record.Click += (_, _) => { if (_recorder.IsRecording) StopRecording(); else StartRecording(); };
        _keyboard.Checked += OptionsChanged; _keyboard.Unchecked += OptionsChanged;
        _gamepad.Checked += OptionsChanged; _gamepad.Unchecked += OptionsChanged;
        _swap.Checked += OptionsChanged; _swap.Unchecked += OptionsChanged;
        _slot.SelectionChanged += OptionsChanged;
        _gamepadTimer.Tick += (_, _) => PollGamepad();
        _hook.Failed += error => Dispatcher.BeginInvoke(() => PauseAsync("键盘捕获已停止：" + error.Message));
        _pump.Failed += error =>
        {
            if (Interlocked.Exchange(ref _pumpFaulted, 1) == 0)
                Dispatcher.BeginInvoke(() => PauseAsync("控制已暂停，请检查连接并重新打开窗口：" + error.Message));
        };
        _pump.Confirmed += state =>
        {
            lock (_confirmedGate) { _lastConfirmed = state; _recorder.Record(state); }
            Dispatcher.BeginInvoke(() => { if (!_closing) ShowState(state); });
        };
        Deactivated += async (_, _) => { if (!_closing) await PauseAsync("窗口失去焦点，控制已暂停。点击“开始控制”继续。"); };
        Activated += async (_, _) =>
        {
            if (!_startRecordingWhenActive || !_enableNativeInput) return;
            _startRecordingWhenActive = false; await StartAsync(); if (_running) StartRecording();
        };
        Closing += ClosingAsync;
        UpdateControls();
    }

    private void BuildInterface()
    {
        // Refactoring UI §2.1 / §3.6: one primary start action; grouped controller inputs
        // reuse the application's Panel/Page/Accent brushes and 8/16/24 spacing.
        var root = new DockPanel(); Content = root;
        var heading = new StackPanel { Margin = new(24, 24, 24, 16) };
        heading.Children.Add(new TextBlock { Text = "手柄控制与录制", FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(Caption("键盘、屏幕按键与 XInput 手柄可同时控制。只有此窗口获得焦点时生效。", new(0, 8, 0, 0)));
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new StackPanel { Margin = new(24, 16, 24, 20) };
        var actions = new WrapPanel(); _start.Style = (Style)Application.Current.FindResource("PrimaryButton");
        actions.Children.Add(_start); actions.Children.Add(_pause); actions.Children.Add(_record);
        var close = new Button { Content = "关闭" }; close.Click += (_, _) => Close(); actions.Children.Add(close);
        footer.Children.Add(actions); _status.Margin = new(0, 12, 0, 8); footer.Children.Add(_status);
        _recordStatus.Style = (Style)Application.Current.FindResource("Caption"); footer.Children.Add(_recordStatus);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel { Margin = new(24, 0, 24, 0) };
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var source = new StackPanel(); var sourceRow = new WrapPanel { Margin = new(0, 0, 0, 8) };
        sourceRow.Children.Add(_keyboard); sourceRow.Children.Add(_gamepad); sourceRow.Children.Add(_slot);
        var mapping = new Button { Content = "设置键盘映射" }; mapping.Click += async (_, _) => await EditMappingAsync(); sourceRow.Children.Add(mapping);
        source.Children.Add(sourceRow);
        var sourceDetails = new StackPanel(); sourceDetails.Children.Add(_swap);
        _gamepadStatus.Margin = new(0, 8, 0, 0); sourceDetails.Children.Add(_gamepadStatus);
        sourceDetails.Children.Add(Caption("实体手柄需支持 XInput。HOME / 截图可用键盘映射或下方屏幕按键。", new(0, 8, 0, 0)));
        source.Children.Add(new Expander { Header = "手柄映射与连接信息", Content = sourceDetails });
        body.Children.Add(Card(source, new(0, 0, 0, 16)));

        var pad = new Grid(); pad.ColumnDefinitions.Add(new() { Width = new GridLength(132) });
        pad.ColumnDefinitions.Add(new()); pad.ColumnDefinitions.Add(new() { Width = new GridLength(132) });
        for (int row = 0; row < 3; row++) pad.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var leftShoulder = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 16) };
        leftShoulder.Children.Add(TargetButton("ZL", "ZL", 56)); leftShoulder.Children.Add(TargetButton("L", "L", 56)); pad.Children.Add(leftShoulder);
        var rightShoulder = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 16) };
        rightShoulder.Children.Add(TargetButton("R", "R", 56)); rightShoulder.Children.Add(TargetButton("ZR", "ZR", 56)); Grid.SetColumn(rightShoulder, 2); pad.Children.Add(rightShoulder);
        var leftStick = DirectionGroup("左摇杆 · LS", "LS_"); Grid.SetRow(leftStick, 1); pad.Children.Add(leftStick);
        var faces = new Grid { Width = 132, Height = 132, HorizontalAlignment = HorizontalAlignment.Center };
        for (int i = 0; i < 3; i++) { faces.ColumnDefinitions.Add(new()); faces.RowDefinitions.Add(new()); }
        Place(faces, TargetButton("X", "X"), 1, 0); Place(faces, TargetButton("Y", "Y"), 0, 1);
        Place(faces, TargetButton("A", "A"), 2, 1); Place(faces, TargetButton("B", "B"), 1, 2);
        Grid.SetColumn(faces, 2); Grid.SetRow(faces, 1); pad.Children.Add(faces);
        var system = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new(16, 0, 16, 0) };
        var signs = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 12) };
        signs.Children.Add(TargetButton("MINUS", "−")); signs.Children.Add(TargetButton("PLUS", "＋")); system.Children.Add(signs);
        system.Children.Add(TargetButton("HOME", "HOME", 112)); system.Children.Add(TargetButton("CAPTURE", "截图", 112));
        Grid.SetColumn(system, 1); Grid.SetRow(system, 1); pad.Children.Add(system);
        var dpad = DirectionGroup("方向键", ""); Grid.SetRow(dpad, 2); pad.Children.Add(dpad);
        var rightStick = DirectionGroup("右摇杆 · RS", "RS_"); Grid.SetColumn(rightStick, 2); Grid.SetRow(rightStick, 2); pad.Children.Add(rightStick);
        var clicks = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(16, 0, 16, 0) };
        clicks.Children.Add(TargetButton("LCLICK", "左摇杆按下", 136)); clicks.Children.Add(TargetButton("RCLICK", "右摇杆按下", 136));
        clicks.Children.Add(Caption("屏幕按键：按住保持，松开释放", new(0, 12, 0, 0)));
        Grid.SetColumn(clicks, 1); Grid.SetRow(clicks, 2); pad.Children.Add(clicks);
        body.Children.Add(Card(pad, new(0)));
        _axes.Style = (Style)Application.Current.FindResource("Caption"); _axes.Margin = new(0, 12, 0, 0); body.Children.Add(_axes);
    }

    private Button TargetButton(string target, string title, double width = 36)
    {
        var button = new Button { Content = title, Tag = target, Width = width, Height = 36, Padding = new(4), Margin = new(4), FontWeight = FontWeights.SemiBold, Focusable = false, ToolTip = target };
        _targetButtons.Add(target, button);
        if (FaceColors.TryGetValue(target, out var colors)) { button.Background = colors.Background; button.Foreground = colors.Foreground; }
        button.PreviewMouseLeftButtonDown += (_, e) => { if (!_running) return; button.CaptureMouse(); SetScreenTarget(target, true); e.Handled = true; };
        button.PreviewMouseLeftButtonUp += (_, e) => { SetScreenTarget(target, false); if (button.IsMouseCaptured) button.ReleaseMouseCapture(); e.Handled = true; };
        button.LostMouseCapture += (_, _) => SetScreenTarget(target, false);
        return button;
    }

    private StackPanel DirectionGroup(string title, string prefix)
    {
        var group = new StackPanel { Margin = new(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        group.Children.Add(Caption(title, new(4, 0, 0, 8)));
        var grid = new Grid { Width = 132, Height = 132 };
        for (int i = 0; i < 3; i++) { grid.ColumnDefinitions.Add(new()); grid.RowDefinitions.Add(new()); }
        string[] directions = ["UPLEFT", "UP", "UPRIGHT", "LEFT", "RESET", "RIGHT", "DOWNLEFT", "DOWN", "DOWNRIGHT"];
        string[] symbols = ["↖", "↑", "↗", "←", "○", "→", "↙", "↓", "↘"];
        for (int i = 0; i < directions.Length; i++)
        {
            if (prefix.Length == 0 && directions[i] == "RESET") continue;
            Place(grid, TargetButton(prefix + directions[i], symbols[i]), i % 3, i / 3);
        }
        group.Children.Add(grid); return group;
    }

    private static void Place(Grid grid, UIElement item, int column, int row) { Grid.SetColumn(item, column); Grid.SetRow(item, row); grid.Children.Add(item); }
    private static Border Card(UIElement child, Thickness margin) => new() { Style = (Style)Application.Current.FindResource("Panel"), Child = child, Margin = margin };
    private static TextBlock Caption(string text, Thickness margin) => new() { Text = text, Style = (Style)Application.Current.FindResource("Caption"), TextWrapping = TextWrapping.Wrap, Margin = margin };
    private bool HasControlFocus => !_enableNativeInput || IsActive;

    private async Task StartAsync()
    {
        if (_closing || _running || _pumpFaulted != 0 || !HasControlFocus) return;
        int generation = ++_generation; _start.IsEnabled = false;
        await _lifecycle.WaitAsync();
        try
        {
            await _pump.PauseAsync();
            if (_closing || !HasControlFocus || generation != _generation) return;
            _keys.Clear(); _screenTargets.Clear(); _physical = PadState.Neutral;
            if (_settings.KeyboardEnabled && _enableNativeInput) _hook.Start(new WindowInteropHelper(this).Handle, OnKey);
            _running = true;
            if (_settings.GamepadEnabled && _enableNativeInput) _gamepadTimer.Start();
            _status.Text = "控制中 · 切换窗口会自动暂停并释放";
            SubmitCombined(); PollGamepad();
        }
        catch (Exception error) { _running = false; _hook.Stop(); _gamepadTimer.Stop(); _status.Text = error.Message; }
        finally { _lifecycle.Release(); UpdateControls(); }
    }

    private async Task PauseAsync(string status)
    {
        _generation++; _running = false; _hook.Stop(); _gamepadTimer.Stop();
        _keys.Clear(); _screenTargets.Clear(); _physical = PadState.Neutral; _start.IsEnabled = false;
        foreach (var button in _targetButtons.Values) if (button.IsMouseCaptured) button.ReleaseMouseCapture();
        await _lifecycle.WaitAsync();
        try { await _pump.PauseAsync(); _status.Text = status; }
        catch { _status.Text = "控制已暂停，但设备未确认释放；请检查连接。"; }
        finally { StopRecording(); ShowState(PadState.Neutral); _lifecycle.Release(); UpdateControls(); }
    }

    private void OnKey(int message, IntPtr virtualKey, IntPtr flags)
    {
        if (!_running || !_settings.KeyboardEnabled) return;
        int key = virtualKey.ToInt32(), bits = unchecked((int)flags.ToInt64());
        if (key == 0x10) key = ((bits >> 16) & 255) == 0x36 ? 0xA1 : 0xA0;
        else if (key == 0x11) key = (bits & 0x01000000) != 0 ? 0xA3 : 0xA2;
        else if (key == 0x12) key = (bits & 0x01000000) != 0 ? 0xA5 : 0xA4;
        bool changed = message is 0x100 or 0x104 ? _keys.Add(key) : (message is 0x101 or 0x105) && _keys.Remove(key);
        if (changed) SubmitCombined();
    }

    private void SetScreenTarget(string target, bool pressed)
    {
        if (!_running) return;
        bool changed = pressed ? _screenTargets.Add(target) : _screenTargets.Remove(target);
        if (changed) SubmitCombined();
    }

    private void PollGamepad()
    {
        if (!_running) return;
        if (!HasControlFocus) { _ = PauseAsync("窗口失去焦点，控制已暂停。"); return; }
        if (!_settings.GamepadEnabled) { _gamepadStatus.Text = "XInput 未启用"; return; }
        if (!_enableNativeInput) { _gamepadStatus.Text = "界面检查 · 未读取实体手柄"; return; }
        bool connected = XInputReader.TryRead(_settings.GamepadIndex, out var sample);
        var state = connected ? ControllerMapper.FromXInput(sample, _settings) : PadState.Neutral;
        _gamepadStatus.Text = connected ? $"XInput 手柄 {_settings.GamepadIndex + 1} · 已连接" : $"XInput 手柄 {_settings.GamepadIndex + 1} · 未连接或不支持 XInput";
        if (state != _physical) { _physical = state; SubmitCombined(); }
    }

    private void SubmitCombined()
    {
        if (!_running) return;
        if (!HasControlFocus) { _ = PauseAsync("窗口失去焦点，控制已暂停。"); return; }
        var keyboard = _settings.KeyboardEnabled ? ControllerMapper.FromKeys(_keys, _settings) : PadState.Neutral;
        var state = PadState.Merge(PadState.Merge(keyboard, ControllerMapper.FromTargets(_screenTargets)), _physical);
        if (!_pump.Submit(state)) _ = PauseAsync("发送队列已满，控制已暂停。");
    }

    private void StartRecording()
    {
        if (!_running || _recorder.IsRecording) return;
        lock (_confirmedGate) _recorder.Start(_lastConfirmed);
        _record.Content = "结束录制";
        _recordStatus.Text = "● 正在录制 · 暂停、离开窗口或关闭时会结束，并将代码插入主脚本。";
    }

    private void StopRecording()
    {
        if (!_recorder.IsRecording) return;
        string script; lock (_confirmedGate) script = _recorder.Stop();
        _record.Content = "开始录制";
        if (string.IsNullOrWhiteSpace(script)) { _recordStatus.Text = "录制已结束，未产生手柄指令。"; return; }
        try { _recordedScript(script); _recordStatus.Text = "录制已结束，代码已插入主脚本；不会自动执行。"; }
        catch (Exception error) { _recordStatus.Text = "录制完成，但插入主脚本失败：" + error.Message; }
    }

    private async void OptionsChanged(object sender, RoutedEventArgs e)
    {
        if (_changingOptions || _closing) return;
        await PauseAsync("输入来源已更新，点击“开始控制”继续。");
        _settings.KeyboardEnabled = _keyboard.IsChecked == true; _settings.GamepadEnabled = _gamepad.IsChecked == true;
        _settings.GamepadIndex = Math.Clamp(_slot.SelectedIndex, 0, 3); _settings.SwapFaceButtons = _swap.IsChecked == true;
        SaveOptions(); _gamepadStatus.Text = _settings.GamepadEnabled ? "XInput 将在开始控制后连接" : "XInput 未启用";
    }

    private async Task EditMappingAsync()
    {
        await PauseAsync("正在编辑映射，控制已暂停。");
        if (_closing) return;
        var dialog = new ControllerMappingWindow(this, _settings);
        if (dialog.ShowDialog() == true) { _settings = dialog.Result; if (!SaveOptions()) return; }
        _status.Text = "映射编辑已结束，点击“开始控制”继续。";
    }

    private bool SaveOptions()
    {
        try { _saveSettings(ProjectStore.Clone(_settings)); return true; }
        catch (Exception error) { _status.Text = "配置已更新，但保存失败：" + error.Message; return false; }
    }

    private void ShowState(PadState state)
    {
        foreach (var (target, button) in _targetButtons)
        {
            var sample = ControllerMapper.FromTargets([target]);
            bool active = sample.Buttons != 0 ? (state.Buttons & sample.Buttons) != 0
                : target.StartsWith("LS_", StringComparison.Ordinal) ? !target.EndsWith("RESET", StringComparison.Ordinal) && AxisMatch(state.LX, sample.LX) && AxisMatch(state.LY, sample.LY)
                : target.StartsWith("RS_", StringComparison.Ordinal) ? !target.EndsWith("RESET", StringComparison.Ordinal) && AxisMatch(state.RX, sample.RX) && AxisMatch(state.RY, sample.RY)
                : sample.Hat != 15 && sample.Hat == state.Hat;
            if (FaceColors.TryGetValue(target, out var colors)) button.Background = colors.Background;
            else button.SetResourceReference(BackgroundProperty, active ? "SoftBrush" : "PanelBrush");
            button.SetResourceReference(BorderBrushProperty, active ? "AccentBrush" : "LineBrush");
            button.BorderThickness = new(active ? 3 : 1);
        }
        _axes.Text = $"左摇杆 · ({state.LX}, {state.LY})    右摇杆 · ({state.RX}, {state.RY})";
    }

    private static bool AxisMatch(byte actual, byte expected) => expected == 128 ? Math.Abs(actual - 128) < 16 : expected < 128 ? actual < 112 : actual > 144;
    private void UpdateControls()
    {
        _start.IsEnabled = !_running && !_closing && _pumpFaulted == 0; _pause.IsEnabled = _running && !_closing; _record.IsEnabled = _running && !_closing;
        foreach (var button in _targetButtons.Values) button.IsEnabled = _running && !_closing;
    }

    private async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true; if (_closing) return; _closing = true;
        try { await PauseAsync("已关闭。"); }
        finally
        {
            _hook.Dispose();
            try { await _pump.DisposeAsync(); } catch { }
            _closeReady = true; Close();
        }
    }

    private static Brush ColorBrush(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush;
    }
}
