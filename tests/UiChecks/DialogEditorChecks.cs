using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;

internal static class DialogEditorChecks
{
    public static void Run(MainWindow owner, Action<bool, string> check, Action<Window, string> capture)
    {
        var project = new StudioProject
        {
            Name = "对话框测试",
            Phrases = [new() { Name = "开始", FunctionName = "phrase_first", Text = "First" }, new() { Name = "结束", FunctionName = "phrase_second", Text = "Second" }],
            Shortcuts = [new() { Name = "启动工具", FunctionName = "start_tool", Path = "tool.cmd", Arguments = "--demo", WorkingDirectory = "", WaitForExit = true }],
            Sounds = [new() { Name = "系统提醒", FunctionName = "sound_system" }, new() { Name = "音乐提醒", FunctionName = "sound_file", Source = "File", FilePath = @"C:\demo\notice.wav" }]
        };

        var phrases = Create("ResourceEditorWindow", owner, project, true, "phrase_second");
        Edit(phrases, () =>
        {
            var rows = Named<DataGrid>(phrases, "ResourceRows");
            check(rows.SelectedItem is Phrase { FunctionName: "phrase_second" }, "double-click resource target selects requested phrase");
            Descendants<TextBox>(phrases).Single(t => t.AcceptsReturn).Text = "Draft only";
            capture(phrases, "phrases.png"); Click(phrases, "取消");
        });
        check(project.Phrases[1].Text == "Second", "cancelled phrase edit preserves caller project");

        var shortcuts = Create("ResourceEditorWindow", owner, project, false, "start_tool");
        Edit(shortcuts, () =>
        {
            check(Named<DataGrid>(shortcuts, "ResourceRows").SelectedItem is ScriptShortcut { FunctionName: "start_tool" }, "resource jump selects startup script");
            Field(shortcuts, "参数").Text = "--updated";
            Click(shortcuts, "保存到当前项目");
        });
        check(Result<StudioProject>(shortcuts).Shortcuts[0].Arguments == "--updated" && project.Shortcuts[0].Arguments == "--demo", "resource save returns edited clone with all original fields retained");

        var sounds = Create("SoundEditorWindow", owner, project, "sound_file");
        Edit(sounds, () =>
        {
            var rows = Named<DataGrid>(sounds, "SoundRows"); var source = Named<ComboBox>(sounds, "SoundSource");
            check(rows.SelectedItem is SoundAction { FunctionName: "sound_file" } && Equals(source.SelectedValue, "File"), "sound function jump selects file source");
            rows.SelectedIndex = 0; Pump();
            check(Equals(source.SelectedValue, "System"), "switching sound rows restores independent source");
            source.SelectedValue = "File"; Pump(); Named<TextBox>(sounds, "SoundFile").Text = @"C:\demo\changed.mp3";
            rows.SelectedIndex = 1; Pump();
            check(Named<TextBox>(sounds, "SoundFile").Text == @"C:\demo\notice.wav", "sound source editing does not replace another row path");
            rows.SelectedIndex = 0; Pump();
            check(Named<TextBox>(sounds, "SoundFile").Text == @"C:\demo\changed.mp3", "sound file draft survives row switching");
            capture(sounds, "sounds.png"); Click(sounds, "保存到当前项目");
        });
        var soundResult = Result<StudioProject>(sounds);
        check(soundResult.Sounds[0].Source == "File" && soundResult.Sounds[0].FilePath.EndsWith("changed.mp3") && project.Sounds[0].Source == "System", "sound save returns isolated file configuration without playback");

        var push = new PushNotificationSettings
        {
            TimeoutMs = 10000,
            Channels = [new() { Name = "测试通知", Enabled = true, Method = "POST", Url = "https://example.invalid/notify", Token = "fixture-one", Body = "{\"content\":\"{{content}}\"}", Headers = new() { ["Content-Type"] = "application/json" } }, new() { Name = "备用通知", Enabled = false, Method = "GET", Url = "https://example.invalid/backup", Token = "fixture-two" }]
        };
        var pushDialog = Create("PushSettingsWindow", owner, push);
        Edit(pushDialog, () =>
        {
            var rows = Named<DataGrid>(pushDialog, "PushChannels"); var token = Named<PasswordBox>(pushDialog, "PushToken");
            check(token.Password == "fixture-one", "push token loads in masked password field");
            token.Password = "fixture-changed"; rows.SelectedIndex = 1; Pump();
            check(token.Password == "fixture-two", "switching push services keeps credentials separate");
            rows.SelectedIndex = 0; Pump(); check(token.Password == "fixture-changed", "push credential draft survives selection change");
            capture(pushDialog, "push-settings.png"); Click(pushDialog, "保存推送配置");
        });
        check(Result<PushNotificationSettings>(pushDialog).Channels[0].Token == "fixture-changed" && push.Channels[0].Token == "fixture-one", "push save returns validated clone without changing source or sending requests");
        var cancelledPush = Create("PushSettingsWindow", owner, push);
        Edit(cancelledPush, () => { Named<PasswordBox>(cancelledPush, "PushToken").Password = "discarded"; Click(cancelledPush, "取消"); });
        check(push.Channels[0].Token == "fixture-one", "push cancel preserves existing credentials");
    }

    private static Window Create(string typeName, params object?[] args) => (Window)Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("TextSenderStudio." + typeName, true)!, args)!;
    private static T Result<T>(Window window) => (T)window.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)!.GetValue(window)!;
    private static void Edit(Window dialog, Action action)
    {
        Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            try { dialog.Left = -20000; dialog.Top = -20000; dialog.ShowActivated = false; dialog.UpdateLayout(); action(); }
            catch (Exception error) { failure = error; dialog.Close(); }
        });
        dialog.ShowDialog(); Pump();
        if (failure != null) throw new InvalidOperationException(dialog.Title + " check failed", failure);
    }
    private static void Click(Window dialog, string label) => Descendants<Button>(dialog).First(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static T Named<T>(Window dialog, string name) where T : FrameworkElement => Descendants<T>(dialog).Single(c => c.Name == name);
    private static TextBox Field(Window dialog, string label)
    {
        foreach (var panel in Descendants<Panel>(dialog))
            for (int index = 0; index + 1 < panel.Children.Count; index++)
                if (panel.Children[index] is TextBlock { Text: var text } && text == label && panel.Children[index + 1] is TextBox input) return input;
        throw new InvalidOperationException("Missing dialog field: " + label);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) yield return result;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false); Dispatcher.PushFrame(frame);
    }
}
