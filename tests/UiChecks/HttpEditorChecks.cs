using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;

internal static class HttpEditorChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int Run(MainWindow window, ProjectStore store, string output)
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); count++; Console.WriteLine("PASS " + message); }
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Click(string method) { typeof(MainWindow).GetMethod(method, Hidden)!.Invoke(window, [window, new RoutedEventArgs()]); Pump(); }
        var tab = Control<TabControl>("MainTabs"); tab.SelectedIndex = 3; Pump();
        var toggle = Control<CheckBox>("HttpEditToggle");
        var grid = Control<DataGrid>("HttpSteps");
        var list = Control<ListBox>("HttpList");
        var directory = (string)typeof(MainWindow).GetField("_directory", Hidden)!.GetValue(window)!;
        ObservableCollection<HttpStep> Steps() => (ObservableCollection<HttpStep>)grid.ItemsSource;
        Check(toggle.IsChecked != true && !Control<Button>("NewHttpButton").IsEnabled && grid.IsReadOnly, "HTTP actions start locked");
        Check(!grid.CanUserAddRows && !grid.CanUserSortColumns, "HTTP steps have no placeholder row or sortable execution order");
        Click("NewHttpClick"); Check(list.Items.Count == 0, "locked handler cannot create action");
        toggle.IsChecked = true; Pump(); Click("NewHttpClick");
        Check(list.Items.Count == 1 && grid.Items.Count == 1 && !grid.IsReadOnly, "explicit new action creates one usable default step");
        Control<TextBox>("HttpName").Text = "短按与状态";
        Control<TextBox>("HttpFunction").Text = "robot_shortpress";
        var first = Steps()[0]; first.Path = "/api/cmd?cmd=shortpress";
        first.Method = "POST"; first.Body = "{\"message\":\"hello\"}"; first.ContentType = "application/json"; first.Headers["X-Test"] = "first";
        Click("AddHttpStepClick");
        var second = Steps()[1]; second.Path = "/status"; second.WaitAfterMs = 250;
        Check(grid.SelectedItem == second && Steps().Count == 2, "add step inserts and selects row after current selection");
        Click("MoveHttpStepUpClick");
        Check(ReferenceEquals(Steps()[0], second) && ReferenceEquals(Steps()[1], first) && first.Body.Contains("hello") && first.Headers["X-Test"] == "first", "reorder moves complete step including body and headers");
        Check(!Control<Button>("MoveHttpStepUpButton").IsEnabled && Control<Button>("MoveHttpStepDownButton").IsEnabled, "step order controls follow first-row boundary");
        Click("SaveHttpClick");
        var disk = store.Load(directory).HttpActions.Single();
        Check(disk.FunctionName == "robot_shortpress" && disk.Steps[0].Path == "/status" && disk.Steps[1].Headers["X-Test"] == "first", "save persists step order and hidden request metadata");
        var waitColumn = grid.Columns.OfType<DataGridTextColumn>().Single(c => (c.Binding as Binding)?.Path.Path == nameof(HttpStep.WaitAfterMs));
        grid.SelectedIndex = 0; grid.CurrentCell = new DataGridCellInfo(Steps()[0], waitColumn); grid.Focus();
        Check(grid.BeginEdit(), "HTTP wait cell enters native edit mode"); Pump();
        var waitEditor = waitColumn.GetCellContent(Steps()[0]) as TextBox ?? throw new InvalidOperationException("Missing wait cell editor");
        waitEditor.Text = "invalid"; waitEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Click("MoveHttpStepDownClick");
        Check(Steps()[0].Path == "/status" && Control<TextBlock>("HttpResult").Text.Contains("整数"), "invalid pending numeric cell blocks reorder without losing draft");
        waitEditor.Text = "375"; waitEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Click("MoveHttpStepDownClick");
        Check(Steps()[1].Path == "/status" && Steps()[1].WaitAfterMs == 375, "reorder commits pending numeric edit before moving row");
        Click("MoveHttpStepUpClick");
        Click("NewHttpClick");
        var another = (HttpAction)list.SelectedItem;
        list.SelectedIndex = 0; Pump();
        Control<TextBox>("HttpName").Text = "更新的短按";
        list.SelectedItem = another; Pump();
        Check(store.Load(directory).HttpActions[0].Name == "更新的短按", "switching actions saves valid draft");
        list.SelectedIndex = 0; Pump();
        Steps()[0].Path = "invalid-path";
        list.SelectedItem = another; Pump();
        Check(list.SelectedIndex == 0 && Steps()[0].Path == "invalid-path" && Control<TextBlock>("HttpResult").Text.Contains("未切换"), "invalid draft blocks selection change without discarding text");
        toggle.IsChecked = false; Pump();
        Check(toggle.IsChecked == true && !grid.IsReadOnly && store.Load(directory).HttpActions[0].Steps[0].Path == "/status", "invalid draft keeps editing open and leaves saved action intact");
        Steps()[0].Path = "/status";
        grid.SelectedIndex = 0; Click("DeleteHttpStepClick");
        Check(Steps().Count == 1 && Steps()[0].Path.Contains("shortpress"), "delete removes only selected request");
        Control<TextBox>("HttpBody").SetCurrentValue(TextBox.TextProperty, "{\"message\":\"edited\"}");
        Pump(); toggle.IsChecked = false; Pump();
        Check(grid.IsReadOnly && Control<TextBox>("HttpBody").IsReadOnly && !Control<Button>("DeleteHttpButton").IsEnabled, "locking disables mutations while keeping action visible");
        Check(store.Load(directory).HttpActions[0].Steps[0].Body.Contains("edited"), "closing edit mode saves bound request body");
        Click("AddHttpStepClick"); Click("DeleteHttpClick");
        Check(Steps().Count == 1 && list.Items.Count == 2, "locked handlers cannot modify rows or delete actions");
        var scriptToggle = Control<CheckBox>("MainScriptEditToggle");
        bool? scriptWasEditing = scriptToggle.IsChecked; scriptToggle.IsChecked = false;
        string scriptBefore = Control<ScriptEditorBox>("ScriptEditor").Text;
        Click("InsertHttpClick");
        Check(Control<ScriptEditorBox>("ScriptEditor").Text == scriptBefore, "main script lock blocks HTTP function insertion");
        scriptToggle.IsChecked = true; Click("InsertHttpClick");
        Check(tab.SelectedIndex == 0 && Control<ScriptEditorBox>("ScriptEditor").Text.Contains("CALL robot_shortpress"), "locked action can still insert its script function");
        scriptToggle.IsChecked = scriptWasEditing;
        tab.SelectedIndex = 3; toggle.IsChecked = true; Pump();
        list.SelectedItem = another; Pump(); Click("DeleteHttpClick");
        Check(list.Items.Count == 1 && list.SelectedIndex == 0, "delete action selects remaining item");
        Click("DeleteHttpStepClick"); toggle.IsChecked = false; Pump();
        Check(Steps().Count == 0 && toggle.IsChecked == true && store.Load(directory).HttpActions[0].Steps.Count == 1, "empty action draft stays editable and does not overwrite saved steps");
        Click("AddHttpStepClick"); Steps()[0].Path = "/api/cmd?cmd=shortpress";
        Control<TextBox>("HttpName").Text = "切换项目后保留";
        var projects = Control<ComboBox>("ProjectPicker"); var projectIndex = projects.SelectedIndex;
        projects.SelectedIndex = projectIndex == 0 ? 1 : 0; Pump();
        Check(toggle.IsChecked == false && store.Load(directory).HttpActions[0].Name == "切换项目后保留", "project switch saves HTTP draft and resets edit lock");
        projects.SelectedIndex = projectIndex; Pump();
        Check(Control<TextBox>("HttpName").Text == "切换项目后保留" && Steps()[0].Path.Contains("shortpress"), "returning to project restores saved action and ordered steps");
        toggle.IsChecked = true; Pump();
        Steps()[0].Path = "http://device-one.example:8080/api/cmd?cmd=shortpress";
        Click("AddHttpStepClick"); Steps()[1].Path = "https://device-two.example:8443/status";
        Click("SaveHttpClick");
        var multipleDevices = store.Load(directory).HttpActions[0];
        Check(multipleDevices.Steps[0].Path.StartsWith("http://device-one") && multipleDevices.Steps[1].Path.StartsWith("https://device-two"), "HTTP editor saves different full device URLs without per-action default configuration");
        Control<TextBox>("HttpFunction").Text = "短按电源";
        Control<TextBox>("HttpName").Text = "";
        Click("SaveHttpClick");
        Check(store.Load(directory).HttpActions[0].FunctionName == "短按电源" && store.Load(directory).HttpActions[0].Name == "", "HTTP editor saves Chinese function name with optional description");
        Control<TextBox>("HttpName").Text = "按下电源键后检查设备状态";
        Click("SaveHttpClick");
        window.UpdateLayout();
        Check(Control<TextBox>("HttpFunction").TranslatePoint(new Point(), window).Y < Control<TextBox>("HttpName").TranslatePoint(new Point(), window).Y, "HTTP script function field is above description");
        toggle.IsChecked = false; Pump();
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(Path.Combine(output, "http-actions.png"))) png.Save(stream);
        tab.SelectedIndex = 0; Pump();
        return count;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
