using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;

internal static class RuntimeResourceChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(MainWindow main, ProjectStore store, Action<bool, string> check)
    {
        T Control<T>(string name) where T : FrameworkElement => (T)main.FindName(name);
        object? Field(string name) => typeof(MainWindow).GetField(name, Hidden)!.GetValue(main);
        void Invoke(string method) => typeof(MainWindow).GetMethod(method, Hidden)!.Invoke(main, [main, new RoutedEventArgs()]);
        void Refresh() => typeof(MainWindow).GetMethod("RefreshResources", Hidden)!.Invoke(main, null);
        var directory = (string)Field("_directory")!;
        var project = (StudioProject)Field("_project")!;
        var tabs = Control<TabControl>("MainTabs");
        var list = Control<ListBox>("HttpList");
        var grid = Control<DataGrid>("HttpSteps");
        var toggle = Control<CheckBox>("HttpEditToggle");
        var previousTab = tabs.SelectedIndex;
        var previousAction = list.SelectedItem;
        var previousLabel = Control<ListBox>("LabelsList").SelectedItem;
        var originallyEditing = toggle.IsChecked == true;
        using var http = new HeldLoopbackHttp();
        var clicked = new HttpAction { Name = "运行检查", FunctionName = "双击执行检查", Steps = [new() { Path = http.Url + "/clicked" }] };
        var other = new HttpAction { Name = "运行检查", FunctionName = "双击另一个", Steps = [new() { Path = http.Url + "/other" }] };
        var label = new ImageLabel { Name = "运行锁检查", ImagePath = "labels/absent-fixture.png" };
        project.HttpActions.AddRange([clicked, other]); project.Labels.Add(label);
        store.Save(directory, project); Refresh();
        tabs.SelectedItem = main.FindName("HttpActionsTab"); list.SelectedItem = clicked;
        Control<ListBox>("LabelsList").SelectedItem = label;
        toggle.IsChecked = true; PumpFor(10);
        try
        {
            foreach (var outcome in new[] { "success", "cancel", "failure" })
            {
                Control<TextBox>("HttpName").Text = "未保存说明";
                Control<TextBox>("LabelName").Text = "未保存标签";
                var saved = JsonSerializer.Serialize(store.Load(directory), ProjectStore.Json);
                var actionCount = project.HttpActions.Count;
                var labelCount = project.Labels.Count;
                var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Func<CancellationToken, Task> work = token => pending.Task.WaitAsync(token);
                var running = (Task)typeof(MainWindow).GetMethod("RunOperation", Hidden)!.Invoke(main, [work, false])!;
                try
                {
                    check(!Control<FrameworkElement>("ImageLabelWorkspace").IsEnabled && !Control<Button>("NewImageLabelButton").IsEnabled,
                        "running operation locks image label editing: " + outcome);
                    check(!toggle.IsEnabled && !Control<FrameworkElement>("HttpDetails").IsEnabled && grid.IsReadOnly
                        && !Control<Button>("NewHttpButton").IsEnabled && !Control<Button>("SaveHttpButton").IsEnabled,
                        "running operation locks HTTP editing: " + outcome);
                    toggle.IsChecked = false;
                    check(toggle.IsChecked == true, "runtime toggle cannot change saved edit mode: " + outcome);
                    foreach (var method in new[] { "SaveHttpClick", "NewHttpClick", "AddHttpStepClick", "DeleteHttpStepClick", "DeleteHttpClick", "SaveLabelClick", "DeleteLabelClick", "AdoptLabelRegionClick" }) Invoke(method);
                    check(project.HttpActions.Count == actionCount && project.Labels.Count == labelCount && grid.Items.Count == 1
                        && JsonSerializer.Serialize(store.Load(directory), ProjectStore.Json) == saved,
                        "runtime handler guards preserve saved resources: " + outcome);
                    if (outcome == "cancel") Invoke("StopClick");
                    else if (outcome == "failure") pending.TrySetException(new InvalidOperationException("Simulated operation failure"));
                    else pending.TrySetResult();
                    Until(() => running.IsCompleted); running.GetAwaiter().GetResult();
                    check(Field("_operation") == null && Control<FrameworkElement>("ImageLabelWorkspace").IsEnabled
                        && Control<Button>("NewImageLabelButton").IsEnabled && toggle.IsEnabled && toggle.IsChecked == true
                        && Control<FrameworkElement>("HttpDetails").IsEnabled && !grid.IsReadOnly,
                        "operation end restores prior resource edit mode: " + outcome);
                }
                finally
                {
                    pending.TrySetResult();
                    if (!running.IsCompleted) { Invoke("StopClick"); Until(() => running.IsCompleted); }
                }
            }

            Control<TextBox>("HttpName").Text = clicked.Name;
            Control<TextBox>("LabelName").Text = label.Name;
            toggle.IsChecked = false;
            list.SelectedItem = other;
            main.UpdateLayout(); list.ScrollIntoView(clicked); main.UpdateLayout(); PumpFor(10);
            var item = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(clicked)
                ?? throw new InvalidOperationException("Fixture HTTP action has no list container.");
            void DoubleClick(object source, MouseButton button = MouseButton.Left)
            {
                var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button)
                    { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent, Source = source };
                typeof(MainWindow).GetMethod("HttpActionDoubleClick", Hidden)!.Invoke(main, [list, e]);
            }
            DoubleClick(list); DoubleClick(item, MouseButton.Right);
            check(Field("_operation") == null && http.RequestCount == 0, "HTTP double-click ignores empty space and right button");
            DoubleClick(item);
            Until(() => http.RequestCount == 1);
            check(ReferenceEquals(list.SelectedItem, clicked) && http.RequestLine == "GET /clicked HTTP/1.1" && Field("_operation") != null,
                "HTTP double-click executes clicked item through the operation lifecycle");
            check(!Control<FrameworkElement>("ImageLabelWorkspace").IsEnabled && !toggle.IsEnabled,
                "HTTP double-click also locks image and action editing");
            DoubleClick(item); http.Reply.TrySetResult();
            Until(() => Field("_operation") == null); PumpFor(50);
            check(http.RequestCount == 1 && toggle.IsEnabled && grid.IsReadOnly && toggle.IsChecked == false,
                "busy double-click does not reenter and completion restores locked edit preference");

            toggle.IsChecked = true;
            var steps = (ObservableCollection<HttpStep>)grid.ItemsSource;
            var validPath = steps[0].Path; steps[0].Path = "invalid-path";
            main.UpdateLayout(); list.ScrollIntoView(other); main.UpdateLayout();
            var otherItem = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(other)
                ?? throw new InvalidOperationException("Second fixture HTTP action has no list container.");
            DoubleClick(otherItem); PumpFor(10);
            check(ReferenceEquals(list.SelectedItem, clicked) && Field("_operation") == null && http.RequestCount == 1
                && Control<TextBlock>("HttpResult").Text.Contains("未切换"), "invalid HTTP draft blocks double-click selection and execution");
            steps[0].Path = validPath; toggle.IsChecked = false;
        }
        finally
        {
            http.Reply.TrySetResult();
            if (Field("_operation") != null) { Invoke("StopClick"); Until(() => Field("_operation") == null); }
            // This MainWindow and store belong exclusively to the temporary UI fixture.
            typeof(MainWindow).GetMethod("SetHttpToggle", Hidden)!.Invoke(main, [false]);
            typeof(MainWindow).GetField("_httpEditing", Hidden)!.SetValue(main, false);
            project.HttpActions.Remove(clicked); project.HttpActions.Remove(other); project.Labels.Remove(label);
            store.Save(directory, project); Refresh();
            list.SelectedItem = previousAction; Control<ListBox>("LabelsList").SelectedItem = previousLabel;
            toggle.IsChecked = originallyEditing; tabs.SelectedIndex = previousTab;
        }
    }

    private static void Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 5000) PumpFor(10);
        if (!condition()) throw new InvalidOperationException("Timed out waiting for fixture operation.");
    }

    private static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private sealed class HeldLoopbackHttp : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        private int _requests;
        public int RequestCount => Volatile.Read(ref _requests);
        public string RequestLine { get; private set; } = "";
        public string Url { get; }
        public TaskCompletionSource Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HeldLoopbackHttp()
        {
            _listener.Start(); Url = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _worker = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
                        using var stream = connection.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                        RequestLine = await reader.ReadLineAsync(_stop.Token) ?? "";
                        while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                        Interlocked.Increment(ref _requests);
                        await Reply.Task.WaitAsync(_stop.Token);
                        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), _stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) when (_stop.IsCancellationRequested) { }
            });
        }
        public void Dispose()
        {
            _stop.Cancel(); _listener.Stop(); _worker.GetAwaiter().GetResult(); _stop.Dispose();
        }
    }
}
