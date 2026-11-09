using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;
using TextSenderStudio.Vision;

namespace TextSenderStudio;

public partial class MainWindow : Window
{
    private readonly ProjectStore _store;
    private AppSettings _settings;
    private StudioProject? _project;
    private string _directory = "";
    private IInputBridge _bridge = null!;
    private readonly CaptureService _capture = new();
    private readonly StudioScriptEngine _engine = new();
    private FrameSnapshot? _frame;
    private LanControlServer? _lan;
    private CancellationTokenSource? _operation;
    private TaskCompletionSource? _operationFinished;
    private int _lanGeneration;
    private bool _initializing = true, _captureActive, _captureChanging, _framePending, _closing, _closed;
    private int _mode;
    private string _scriptBuffer = "", _textBuffer = "";
    private ImageLabel? _selectedLabel;
    private HttpAction? _selectedHttp;
    private ObservableCollection<HttpStep> _steps = [];
    private sealed record ProjectChoice(string Directory, string Display) { public override string ToString() => Display; }
    public MainWindow()
    {
        _store = new(App.DataRoot); _settings = _store.LoadSettings(); ProjectStore.ValidateSettings(_settings);
        InitializeComponent(); InitializeScriptEditor(); _bridge = CreateBridge();
        TransportPicker.SelectionChanged += (_, _) => { if (_initializing) return; SerialPicker.Visibility = TransportPicker.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; };
        _capture.FrameReady += frame =>
        {
            if (_closing || _framePending) return; _framePending = true;
            Dispatcher.BeginInvoke(() => { try { if (_closing) return; _frame = frame; CaptureView.SetFrame(frame); CaptureStatus.Text = $"{frame.Width} × {frame.Height} px · {frame.CapturedAt:HH:mm:ss}"; SyncRegion(); } finally { _framePending = false; } });
        };
        _capture.Error += message => Dispatcher.BeginInvoke(() => { Log("画面：" + message); _captureActive = false; CaptureButton.Content = "开始预览"; });
        ApplySettings(); RefreshPorts(); RefreshProjects(_settings.LastProject); _initializing = false;
        StartLan(); Log("就绪。选择项目并连接设备后即可使用。");
    }
    private IInputBridge CreateBridge() => _settings.Transport == "Serial" ? new SerialInputBridge(_settings) : new InputBridge(_settings);
    private async Task ReplaceBridge()
    {
        var old = _bridge; _bridge = CreateBridge(); await old.DisposeAsync(); ConnectionStatus.Text = "ESP32 · 未连接";
    }
    private void Log(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Log(message)); return; }
        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {message}\n"); if (LogBox.Text.Length > 24000) LogBox.Text = LogBox.Text[^16000..]; LogBox.ScrollToEnd();
    }
    private void ApplySettings()
    {
        Topmost = _settings.AlwaysOnTop; TopmostCheck.IsChecked = _settings.AlwaysOnTop;
        ManualPanel.Visibility = _settings.ShowManualControls ? Visibility.Visible : Visibility.Collapsed;
        SendDelay.Text = _settings.SendDelayMs.ToString(); CharacterDelay.Text = _settings.CharacterDelayMs.ToString(); LineDelay.Text = _settings.LineDelayMs.ToString();
        ClearIndent.IsChecked = _settings.ClearAutoIndent; PhraseEnter.IsChecked = _settings.PhraseAutoEnter;
        TransportPicker.SelectedIndex = _settings.Transport == "Serial" ? 1 : 0; SerialPicker.Visibility = TransportPicker.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        RobotAddress.Text = string.IsNullOrWhiteSpace(_settings.RobotHost) ? "默认设备未设置；完整 URL 可直接使用" : _settings.RobotBaseUrl;
    }
    private void ReadTiming()
    {
        _settings.SendDelayMs = NonNegative(SendDelay.Text); _settings.CharacterDelayMs = NonNegative(CharacterDelay.Text); _settings.LineDelayMs = NonNegative(LineDelay.Text);
        _settings.ClearAutoIndent = ClearIndent.IsChecked == true; _settings.PhraseAutoEnter = PhraseEnter.IsChecked == true; _store.SaveSettings(_settings);
    }
    private static int NonNegative(string text) => int.TryParse(text, out var n) && n >= 0 && n <= 600000 ? n : throw new ArgumentException("请输入 0–600000 的整数毫秒。");
    private void RefreshProjects(string? selected)
    {
        var saved = _initializing; _initializing = true;
        var items = new List<ProjectChoice>();
        foreach (var name in _store.ListProjects()) { try { var display = _store.Load(name).Name; items.Add(new(name, display == name ? "projects/" + name : display + " · " + name)); } catch (Exception error) { Log("未载入项目 " + name + "：" + error.Message); } }
        ProjectPicker.ItemsSource = items; ProjectPicker.SelectedItem = items.FirstOrDefault(p => p.Directory == selected) ?? items.FirstOrDefault(); _initializing = saved;
        if (ProjectPicker.SelectedItem is ProjectChoice choice) LoadProject(choice.Directory); else { _project = null; _directory = ""; MainTabs.Visibility = Visibility.Collapsed; EmptyState.Visibility = Visibility.Visible; ProjectLocation.Text = "项目位置：程序目录 / projects"; }
    }
    private void LoadProject(string directory)
    {
        var previous = _initializing; _initializing = true;
        try
        {
            _project = _store.Load(directory); _directory = directory; _settings.LastProject = directory; _store.SaveSettings(_settings);
            _scriptBuffer = ReadFile(_store.Resolve(directory, _project.MainScript)); _textBuffer = ReadFile(_store.Resolve(directory, "draft.txt"));
            ScriptEditor.Text = _mode == 0 ? _scriptBuffer : _textBuffer; ScriptFileName.Text = _mode == 0 ? _project.MainScript : "draft.txt"; SaveState.Text = "已载入";
            ProjectLocation.Text = "项目位置：程序目录 / projects/" + directory; ProjectLocation.ToolTip = _store.ProjectPath(directory); MainTabs.Visibility = Visibility.Visible; EmptyState.Visibility = Visibility.Collapsed;
            CaptureSource.SelectedIndex = _project.Capture.Source == "Camera" ? 1 : 0; UpdateCaptureSourceUi(); RefreshResources(); SyncRegion();
        }
        finally { _initializing = previous; }
    }
    private static string ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
    private void SaveProject()
    {
        if (_project == null) return; SavePendingHttp(); ReadTiming();
        if (_mode == 0) _scriptBuffer = ScriptEditor.Text; else _textBuffer = ScriptEditor.Text;
        File.WriteAllText(_store.Resolve(_directory, _project.MainScript), _scriptBuffer, new UTF8Encoding(false));
        File.WriteAllText(_store.Resolve(_directory, "draft.txt"), _textBuffer, new UTF8Encoding(false));
        _store.Save(_directory, _project); SaveState.Text = "已保存";
    }
    private void RefreshResources()
    {
        if (_project == null) return; PhrasesPanel.Children.Clear(); ShortcutsPanel.Children.Clear();
        foreach (var phrase in _project.Phrases) AddActionButton(PhrasesPanel, phrase.Name, phrase.FunctionName, true);
        foreach (var shortcut in _project.Shortcuts) AddActionButton(ShortcutsPanel, shortcut.Name, shortcut.FunctionName, false);
        LabelsList.ItemsSource = null; LabelsList.ItemsSource = _project.Labels; LabelsList.SelectedIndex = _project.Labels.Count > 0 ? 0 : -1; LabelDetails.Visibility = _project.Labels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshHttpActions();
        RefreshEditorContext();
        LabelEmpty.Visibility = _project.Labels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void AddActionButton(Panel panel, string name, string function, bool input)
    {
        var button = new Button { Content = name, Margin = new(0, 0, 8, 8) }; button.Click += async (_, _) => await RunAction(function, input); panel.Children.Add(button);
    }
    private async void ProjectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ProjectPicker.SelectedItem is not ProjectChoice choice || choice.Directory == _directory) return;
        try { SaveProject(); if (_captureActive) await StopCapture(); LoadProject(choice.Directory); }
        catch (Exception error) { Dialogs.Error(this, error); var previous = _initializing; _initializing = true; ProjectPicker.SelectedItem = ProjectPicker.Items.Cast<ProjectChoice>().FirstOrDefault(p => p.Directory == _directory); _initializing = previous; }
    }
    private void NewProjectClick(object sender, RoutedEventArgs e)
    {
        var name = Dialogs.Prompt(this, "新增项目", "项目文件夹名称", "新项目"); if (name == null) return;
        try { SaveProject(); _store.Create(name); RefreshProjects(name); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void RenameProjectClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return; var name = Dialogs.Prompt(this, "项目名称", "显示名称（文件夹位置不变）", _project.Name); if (string.IsNullOrWhiteSpace(name)) return;
        try { _project.Name = name; SaveProject(); RefreshProjects(_directory); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private async void DeleteProjectClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _operation != null) return;
        if (MessageBox.Show(this, $"将项目“{_project.Name}”移入回收站？\n{_store.ProjectPath(_directory)}\n被引用的外部脚本不会删除。", "删除项目", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { if (_captureActive) await StopCapture(); _store.DeleteToRecycleBin(_directory); _project = null; _directory = ""; RefreshProjects(null); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void OpenProjectFolderClick(object sender, RoutedEventArgs e) { if (_project != null) Process.Start(new ProcessStartInfo(_store.ProjectPath(_directory)) { UseShellExecute = true }); }
    private async void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (_operation != null) { Log("请先停止正在执行的操作。"); return; }
        var dialog = new SettingsWindow(this, _settings, async (config, ssid, password) => { await using var bridge = new InputBridge(config); await bridge.ConfigureWifiAsync(ssid, password, CancellationToken.None); });
        if (dialog.ShowDialog() != true) return;
        try { _store.SaveSettings(dialog.Result); _settings = dialog.Result; ApplySettings(); await ReplaceBridge(); StartLan(); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void LanSettingsClick(object sender, RoutedEventArgs e) => SettingsClick(sender, e);
    private void TopmostChanged(object sender, RoutedEventArgs e) { if (_initializing) return; _settings.AlwaysOnTop = TopmostCheck.IsChecked == true; Topmost = _settings.AlwaysOnTop; _store.SaveSettings(_settings); }
    private void RefreshPorts() { SerialPicker.ItemsSource = SerialPort.GetPortNames().Order().ToArray(); SerialPicker.SelectedItem = _settings.SerialPort; }
    private void RefreshPortsClick(object sender, RoutedEventArgs e) => RefreshPorts();
    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        try { var next = ProjectStore.Clone(_settings); next.Transport = TransportPicker.SelectedIndex == 1 ? "Serial" : "WiFi"; next.SerialPort = SerialPicker.SelectedItem as string ?? ""; if (next.Transport == "Serial" && next.SerialPort.Length == 0) throw new ArgumentException("请先选择串口。"); _store.SaveSettings(next); _settings = next; await ReplaceBridge(); var status = await _bridge.StatusAsync(CancellationToken.None); ConnectionStatus.Text = _settings.Transport == "Serial" ? _settings.SerialPort + " · 串口已响应" : _settings.EspBaseUrl + " · 已响应"; Log(status); }
        catch (Exception error) { ConnectionStatus.Text = "连接失败"; Log(error.Message); Dialogs.Error(this, error); }
    }
    private ProjectActions Actions(StudioProject snapshot) => new(ProjectStore.Clone(_settings), snapshot, _store.ProjectPath(_directory), _bridge, async (name, token) =>
    {
        token.ThrowIfCancellationRequested(); var label = snapshot.Labels.FirstOrDefault(l => l.Name == name) ?? throw new InvalidOperationException("不存在图像标签：" + name);
        var frame = _capture.GetLatestFrame() ?? throw new InvalidOperationException("请先开始画面预览。");
        if (DateTimeOffset.Now - frame.CapturedAt > TimeSpan.FromSeconds(5)) throw new InvalidOperationException("采集画面已过期，请检查画面来源。");
        return await Task.Run(() => TemplateMatcher.Match(frame, ImageTools.Load(_store.Resolve(_directory, label.ImagePath)), label).Score, token);
    }, Log, (sound, token) => SoundPlayback.PlayAsync(sound, _store.ProjectPath(_directory), token));
    private async Task RunOperation(Func<CancellationToken, Task> work, bool releaseInput = false)
    {
        if (_operation != null) { Log("已有操作正在执行，请先停止。"); return; }
        using var cancellation = new CancellationTokenSource(); _operation = cancellation; _operationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously); ProjectBar.IsEnabled = false; RunButton.IsEnabled = false; StopButton.IsEnabled = true; RunStatus.Text = "执行中";
        try { await work(cancellation.Token); RunStatus.Text = "已完成"; }
        catch (OperationCanceledException) { RunStatus.Text = "已停止"; Log("操作已停止。"); }
        catch (Exception error) { RunStatus.Text = "执行失败"; Log(error.Message); }
        finally
        {
            if (releaseInput) { try { using var limit = new CancellationTokenSource(2500); await _bridge.ReleaseAllAsync(limit.Token); } catch (Exception error) { Log("释放输入：" + error.Message); } }
            _operation = null; ProjectBar.IsEnabled = true; RunButton.IsEnabled = true; StopButton.IsEnabled = false;
            _operationFinished.TrySetResult();
        }
    }
    private async Task RunAction(string function, bool input)
    {
        if (_project == null) return;
        try { ReadTiming(); var actions = Actions(ProjectStore.Clone(_project)); await RunOperation(ct => actions.CallAsync(function, ct), input); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private async void RunClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        try { SaveProject(); var code = ScriptEditor.Text; var actions = Actions(ProjectStore.Clone(_project)); await RunOperation(ct => _mode == 0 ? _engine.RunAsync(code, actions, ct) : actions.TypeAsync(code, ct), true); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void StopClick(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private async void ReleaseClick(object sender, RoutedEventArgs e) { _operation?.Cancel(); try { await _bridge.ReleaseAllAsync(CancellationToken.None); Log("已释放输入。"); } catch (Exception error) { Log(error.Message); } }
    private void ScriptChanged(object sender, TextChangedEventArgs e) { if (_initializing) return; SaveState.Text = "未保存"; }
    private void EditorModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ScriptEditor == null || _mode == EditorMode.SelectedIndex) return;
        if (_mode == 0) _scriptBuffer = ScriptEditor.Text; else _textBuffer = ScriptEditor.Text; _mode = EditorMode.SelectedIndex;
        ScriptEditor.Text = _mode == 0 ? _scriptBuffer : _textBuffer; ScriptFileName.Text = _mode == 0 ? _project?.MainScript ?? "main.ecs" : "draft.txt"; RunButton.Content = _mode == 0 ? "运行脚本" : "发送文字";
        RefreshEditorContext();
    }
    private void LoadScriptClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return; var picker = new OpenFileDialog { Filter = "脚本 / 文本|*.ecs;*.txt;*.bas|所有文件|*.*", InitialDirectory = _store.ProjectPath(_directory) };
        if (picker.ShowDialog(this) != true) return;
        try { SaveProject(); if (Path.GetExtension(picker.FileName).Equals(".ecs", StringComparison.OrdinalIgnoreCase)) { _project.MainScript = Path.GetFileName(picker.FileName); EditorMode.SelectedIndex = 0; } else EditorMode.SelectedIndex = 1; ScriptEditor.Text = File.ReadAllText(picker.FileName, Encoding.UTF8); ScriptFileName.Text = _mode == 0 ? _project.MainScript : "draft.txt"; SaveProject(); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void SaveScriptClick(object sender, RoutedEventArgs e) { try { SaveProject(); } catch (Exception error) { Dialogs.Error(this, error); } }
    private void InsertCode(string text) { MainTabs.SelectedIndex = 0; EditorMode.SelectedIndex = 0; ScriptEditor.AppendText("\n" + text + "\n"); ScriptEditor.Focus(); ScriptEditor.CaretIndex = ScriptEditor.Text.Length; }
    private void InsertFunctionClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return; var names = Actions(_project).FunctionNames;
        if (names.Count == 0) { Log("请先添加短语、启动脚本、声音提醒或 HTTP 动作。"); return; }
        var dialog = Dialogs.Window(this, "插入函数"); var panel = new StackPanel { Margin = new(24) }; var list = new ComboBox { ItemsSource = names, SelectedIndex = 0, Margin = new(0, 0, 0, 20) }; panel.Children.Add(list); var actions = new WrapPanel(); panel.Children.Add(actions); Dialogs.Button(actions, "取消", () => dialog.DialogResult = false); Dialogs.Button(actions, "插入", () => dialog.DialogResult = true, true); dialog.Content = panel;
        if (dialog.ShowDialog() == true) InsertCode("CALL " + list.SelectedItem);
    }
    private void ManagePhrasesClick(object sender, RoutedEventArgs e) => ManageResources(true);
    private void ManageShortcutsClick(object sender, RoutedEventArgs e) => ManageResources(false);
    private void ManageResources(bool phrases)
    {
        if (_project == null || _operation != null) return;
        try
        {
            SaveProject(); var editor = new ResourceEditorWindow(this, _project, phrases);
            if (editor.ShowDialog() == true) { _store.Save(_directory, editor.Result); _project = editor.Result; RefreshResources(); }
        }
        catch (Exception error) { Dialogs.Error(this, error); }
    }
    private async void PassthroughClick(object sender, RoutedEventArgs e)
    {
        if (_operation != null) return;
        try { _ = await _bridge.StatusAsync(CancellationToken.None); var dialog = new PassthroughWindow(this, _bridge); dialog.ShowDialog(); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private async void CapsClick(object sender, RoutedEventArgs e) => await RunOperation(ct => _bridge.TapKeyAsync("CAPSLOCK", 100, ct), true);
    private async void ManualPadClick(object sender, RoutedEventArgs e) { try { var ms = NonNegative(KeyDuration.Text); await RunOperation(ct => _bridge.PressPadAsync((string)((Button)sender).Tag, ms, ct), true); } catch (Exception error) { Dialogs.Error(this, error); } }
    private async void ManualKeyClick(object sender, RoutedEventArgs e) { try { var ms = NonNegative(KeyDuration.Text); await RunOperation(ct => _bridge.TapKeyAsync((string)((Button)sender).Tag, ms, ct), true); } catch (Exception error) { Dialogs.Error(this, error); } }
    private async void ManualTextClick(object sender, RoutedEventArgs e) { if (_project == null) return; try { ReadTiming(); await RunOperation(ct => Actions(_project).TypeAsync(ManualText.Text, ct), true); } catch (Exception error) { Dialogs.Error(this, error); } }

    private void CaptureSourceChanged(object sender, SelectionChangedEventArgs e) { if (_initializing) return; UpdateCaptureSourceUi(); }
    private void UpdateCaptureSourceUi() { CameraPicker.Visibility = CaptureSource.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; DesktopRegionButton.Visibility = CaptureSource.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; }
    private async void RefreshCamerasClick(object sender, RoutedEventArgs e)
    {
        try { CameraPicker.ItemsSource = await CaptureService.EnumerateCamerasAsync(CancellationToken.None); CameraPicker.SelectedValue = _project?.Capture.CameraIndex ?? 0; if (CameraPicker.SelectedIndex < 0 && CameraPicker.Items.Count > 0) CameraPicker.SelectedIndex = 0; Log($"发现 {CameraPicker.Items.Count} 个采集设备。"); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private async void CaptureClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _captureChanging) return; _captureChanging = true; CaptureButton.IsEnabled = false;
        try
        {
            if (_captureActive) { await StopCapture(); return; }
            _project.Capture.Source = CaptureSource.SelectedIndex == 1 ? "Camera" : "Desktop";
            _project.Capture.CameraIndex = CameraPicker.SelectedValue is int index ? index : 0; _store.Save(_directory, _project);
            CaptureStatus.Text = "正在打开画面…"; await _capture.StartAsync(ProjectStore.Clone(_project.Capture), CancellationToken.None); _captureActive = true; CaptureButton.Content = "停止预览";
        }
        catch (Exception error) { CaptureStatus.Text = error.Message; Log(error.Message); }
        finally { _captureChanging = false; CaptureButton.IsEnabled = true; }
    }
    private async Task StopCapture() { await _capture.StopAsync(); _captureActive = false; CaptureButton.Content = "开始预览"; CaptureStatus.Text = "预览已停止"; }
    private async void DesktopRegionClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return; var wasActive = _captureActive; if (wasActive) await StopCapture();
        Hide(); var picker = new ScreenRegionWindow(); var accepted = picker.ShowDialog(); Show(); Activate();
        if (accepted == true) { _project.Capture.DesktopRegion = picker.Region; _project.Capture.RecognitionRegion = null; _store.Save(_directory, _project); CaptureStatus.Text = "桌面采集区域：" + picker.Region; SyncRegion(); }
    }
    private void SelectRegionClick(object sender, RoutedEventArgs e) { CaptureView.SelectionEnabled = true; RegionStatus.Text = "在画面中按住并拖动，松开完成。"; }
    private void RecognitionRegionChanged(object? sender, RegionChangedEventArgs e) { if (_project == null) return; _project.Capture.RecognitionRegion = e.Region; _initializing = true; CustomRegion.IsChecked = true; _initializing = false; _store.Save(_directory, _project); SyncRegion(); }
    private void RegionModeChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _project == null) return;
        _project.Capture.RecognitionRegion = FullRegion.IsChecked == true ? null : new() { X = 0, Y = 0, Width = _frame?.Width ?? 1920, Height = _frame?.Height ?? 1080 }; _store.Save(_directory, _project); SyncRegion();
    }
    private void SyncRegion()
    {
        if (_project == null) return; var saved = _initializing; _initializing = true;
        var region = _project.Capture.RecognitionRegion; FullRegion.IsChecked = region == null; CustomRegion.IsChecked = region != null;
        var view = region ?? new() { Width = _frame?.Width ?? 1920, Height = _frame?.Height ?? 1080 };
        if (!RoiX.IsKeyboardFocusWithin && !RoiY.IsKeyboardFocusWithin && !RoiW.IsKeyboardFocusWithin && !RoiH.IsKeyboardFocusWithin) { RoiX.Text = view.X.ToString(); RoiY.Text = view.Y.ToString(); RoiW.Text = view.Width.ToString(); RoiH.Text = view.Height.ToString(); }
        foreach (var input in new[] { RoiX, RoiY, RoiW, RoiH }) input.IsEnabled = region != null;
        CaptureView.Region = region; RegionStatus.Text = region == null ? "在整幅画面中查找标签。" : "画面内像素坐标 · " + region; _initializing = saved;
    }
    private void RoiFieldsChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _project == null || FullRegion.IsChecked == true) return;
        try { var r = new PixelRegion { X = int.Parse(RoiX.Text), Y = int.Parse(RoiY.Text), Width = int.Parse(RoiW.Text), Height = int.Parse(RoiH.Text) }; if (r.X < 0 || r.Y < 0 || r.Width < 1 || r.Height < 1 || (long)r.X + r.Width > (_frame?.Width ?? 1920) || (long)r.Y + r.Height > (_frame?.Height ?? 1080)) throw new ArgumentException("识图区域须位于画面边界内。"); _project.Capture.RecognitionRegion = r; _store.Save(_directory, _project); SyncRegion(); } catch (Exception error) { RegionStatus.Text = error.Message; }
    }
    private void NewLabelClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return; var frame = _capture.GetLatestFrame(); if (frame == null) { MessageBox.Show(this, "请先开始画面预览。", "制作标签"); return; }
        var region = Dialogs.PickRegion(this, frame); if (region == null) return; var name = Dialogs.Prompt(this, "制作标签", "标签名称", "新标签"); if (string.IsNullOrWhiteSpace(name)) return;
        try { if (_project.Labels.Any(l => l.Name == name)) throw new ArgumentException("标签名称已经存在。"); var label = new ImageLabel { Name = name, ImagePath = "labels/" + Guid.NewGuid().ToString("N") + ".png", SearchRegion = _project.Capture.RecognitionRegion == null ? null : ProjectStore.Clone(_project.Capture.RecognitionRegion) }; ImageTools.Save(ImageTools.Crop(frame, region), _store.Resolve(_directory, label.ImagePath)); _project.Labels.Add(label); _store.Save(_directory, _project); RefreshResources(); MainTabs.SelectedIndex = 1; LabelsList.SelectedItem = label; } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void LabelSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedLabel = LabelsList.SelectedItem as ImageLabel; LabelDetails.Visibility = _selectedLabel == null ? Visibility.Collapsed : Visibility.Visible; if (_selectedLabel == null) return;
        LabelName.Text = _selectedLabel.Name; LabelThreshold.Text = (_selectedLabel.Threshold * 100).ToString("0.##"); LabelGrayscale.IsChecked = _selectedLabel.Grayscale; LabelRegion.Text = "搜索区域：" + (_selectedLabel.SearchRegion?.ToString() ?? "全画面");
        try { LabelImage.Source = ImageRegionView.Bitmap(ImageTools.Load(_store.Resolve(_directory, _selectedLabel.ImagePath)).Png); } catch (Exception error) { LabelImage.Source = null; LabelResult.Text = error.Message; }
    }
    private void SaveLabel()
    {
        if (_project == null || _selectedLabel == null) return; var name = LabelName.Text.Trim(); if (name.Length == 0 || _project.Labels.Any(l => l != _selectedLabel && l.Name == name)) throw new ArgumentException("标签名不能为空或重复。");
        if (!double.TryParse(LabelThreshold.Text, out var threshold) || threshold < 0 || threshold > 100) throw new ArgumentException("阈值范围为 0–100。");
        _selectedLabel.Name = name; _selectedLabel.Threshold = threshold / 100; _selectedLabel.Grayscale = LabelGrayscale.IsChecked == true; _store.Save(_directory, _project); LabelsList.Items.Refresh(); LabelResult.Text = "标签已保存。";
    }
    private void SaveLabelClick(object sender, RoutedEventArgs e) { try { SaveLabel(); } catch (Exception error) { Dialogs.Error(this, error); } }
    private void AdoptLabelRegionClick(object sender, RoutedEventArgs e) { if (_selectedLabel == null || _project == null) return; _selectedLabel.SearchRegion = _project.Capture.RecognitionRegion == null ? null : ProjectStore.Clone(_project.Capture.RecognitionRegion); _store.Save(_directory, _project); LabelRegion.Text = "搜索区域：" + (_selectedLabel.SearchRegion?.ToString() ?? "全画面"); }
    private async void TestLabelClick(object sender, RoutedEventArgs e)
    {
        if (_selectedLabel == null) return;
        try { SaveLabel(); var frame = _capture.GetLatestFrame() ?? throw new InvalidOperationException("请先开始画面预览。"); var template = ImageTools.Load(_store.Resolve(_directory, _selectedLabel.ImagePath)); var label = ProjectStore.Clone(_selectedLabel); var result = await Task.Run(() => TemplateMatcher.Match(frame, template, label)); LabelResult.Text = $"匹配度 {result.Score * 100:F1}% · {(result.IsMatch ? "达到阈值" : "未达到阈值")} · {result.Bounds}"; } catch (Exception error) { LabelResult.Text = error.Message; }
    }
    private void InsertLabelClick(object sender, RoutedEventArgs e) { if (_selectedLabel != null) InsertCode("$score = @" + _selectedLabel.Name); }
    private void DeleteLabelClick(object sender, RoutedEventArgs e) { if (_project == null || _selectedLabel == null) return; _project.Labels.Remove(_selectedLabel); _store.Save(_directory, _project); RefreshResources(); }
    private sealed record LabelBundle(ImageLabel Label, string PngBase64);
    private void ExportLabelClick(object sender, RoutedEventArgs e)
    {
        if (_selectedLabel == null) return; var picker = new SaveFileDialog { Filter = "TextSender 图像标签|*.tslabel", FileName = _selectedLabel.Name + ".tslabel" }; if (picker.ShowDialog(this) != true) return;
        try { SaveLabel(); ProjectStore.Write(picker.FileName, new LabelBundle(ProjectStore.Clone(_selectedLabel), Convert.ToBase64String(File.ReadAllBytes(_store.Resolve(_directory, _selectedLabel.ImagePath))))); } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void ImportLabelClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return; var picker = new OpenFileDialog { Filter = "TextSender 图像标签 / 图片|*.tslabel;*.png;*.jpg;*.bmp" }; if (picker.ShowDialog(this) != true) return;
        try
        {
            ImageLabel label; FrameSnapshot image;
            if (Path.GetExtension(picker.FileName).Equals(".tslabel", StringComparison.OrdinalIgnoreCase)) { var bundle = ProjectStore.Read<LabelBundle>(picker.FileName) ?? throw new IOException("标签文件无效。"); label = bundle.Label; var bytes = Convert.FromBase64String(bundle.PngBase64); var bitmap = ImageRegionView.Bitmap(bytes); image = new(bytes, bitmap.PixelWidth, bitmap.PixelHeight, DateTimeOffset.Now); }
            else { label = new() { Name = Path.GetFileNameWithoutExtension(picker.FileName) }; image = ImageTools.Load(picker.FileName); }
            if (_project.Labels.Any(l => l.Name == label.Name)) label.Name = Dialogs.Prompt(this, "同名标签", "输入新标签名", label.Name + "_2") ?? throw new OperationCanceledException();
            if (string.IsNullOrWhiteSpace(label.Name) || _project.Labels.Any(l => l.Name == label.Name)) throw new ArgumentException("标签名称不能为空或重复。");
            label.ImagePath = "labels/" + Guid.NewGuid().ToString("N") + ".png"; ImageTools.Save(image, _store.Resolve(_directory, label.ImagePath)); _project.Labels.Add(label); _store.Save(_directory, _project); RefreshResources(); LabelsList.SelectedItem = label;
        }
        catch (OperationCanceledException) { } catch (Exception error) { Dialogs.Error(this, error); }
    }
    private void StartLan()
    {
        var generation = ++_lanGeneration; _lan?.Dispose(); _lan = null; if (!_settings.LanEnabled) return;
        try
        {
            _lan = new(IPAddress.Parse(_settings.LanBindAddress), _settings.LanPort, (route, request) => Dispatcher.InvokeAsync(() =>
            {
                if (_closing || generation != _lanGeneration || !_settings.LanEnabled || _project == null) return new LanControlResponse(409, new { error = "没有活动项目或接口已关闭。" });
                if (route == "items") return new LanControlResponse(200, new { project = _directory, phrases = _project.Phrases.Select(p => p.Name).ToArray(), scripts = _project.Shortcuts.Select(s => s.Name).ToArray() });
                if (_operation != null) return new LanControlResponse(409, new { error = "正在执行其他操作。" });
                if (!string.IsNullOrEmpty(request.Project) && request.Project != _directory) return new LanControlResponse(409, new { error = "指定项目不是当前项目。" });
                var function = request.Kind == "phrase" ? _project.Phrases.FirstOrDefault(p => p.Name == request.Name)?.FunctionName : _project.Shortcuts.FirstOrDefault(s => s.Name == request.Name)?.FunctionName;
                if (function == null) return new LanControlResponse(404, new { error = "没有同名配置动作。" });
                _ = RunAction(function, request.Kind == "phrase"); return new LanControlResponse(202, new { accepted = true });
            }).Task); _lan.Start(); Log($"局域网接口：http://{_settings.LanBindAddress}:{_lan.Port}");
        }
        catch (Exception error) { Log("局域网接口未启动：" + error.Message); }
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closed) return; e.Cancel = true; if (_closing) return;
        try { SaveProject(); } catch (Exception error) { if (MessageBox.Show(this, "保存失败：" + error.Message + "\n仍然退出？", "退出", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; }
        _closing = true; ++_lanGeneration; _operation?.Cancel(); _lan?.Dispose(); if (_operationFinished != null) await Task.WhenAny(_operationFinished.Task, Task.Delay(6000)); await _capture.DisposeAsync(); await _bridge.DisposeAsync(); _closed = true;
        // Idle cleanup may complete synchronously. Let this Closing event return
        // before closing again; WPF rejects a reentrant Close call.
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
