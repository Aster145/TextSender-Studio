using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

internal static class Program
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static Exception? _automationError;

    [STAThread]
    private static int Main(string[] args)
    {
        string repo = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string output = Path.Combine(repo, "work", "ui-checks");
        string fixture = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        MainWindow? main = null;
        App? app = null;
        try
        {
            var store = new ProjectStore(fixture);
            var first = store.Create("01-主项目"); first.Phrases.Add(new() { Name = "主项目短语", FunctionName = "phrase_first", Text = "Hello" }); store.Save("01-主项目", first);
            var second = store.Create("02-输入测试"); second.Phrases.Add(new() { Name = "另一个短语", FunctionName = "phrase_second", Text = "Test" }); store.Save("02-输入测试", second);
            store.SaveSettings(new() { AlwaysOnTop = false, LastProject = "01-主项目" });
            typeof(App).GetProperty(nameof(App.DataRoot))!.SetValue(null, fixture);
            app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            main = new MainWindow { Width = 1280, Height = 900, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false };
            main.Show(); Pump();
            Check(Control<TabControl>(main, "MainTabs").Items.Count == 3, "exactly three primary tabs");
            Check(Control<FrameworkElement>(main, "ManualPanel").Visibility == Visibility.Collapsed, "manual controller panel starts hidden");
            Check(Control<Panel>(main, "PhrasesPanel").Children.OfType<Button>().Single().Content?.ToString() == "主项目短语", "current project phrases loaded");
            var editor = Control<TextBox>(main, "ScriptEditor"); editor.Text = "PRINT 项目一已保存\nWAIT 0";
            var projects = Control<ComboBox>(main, "ProjectPicker"); projects.SelectedIndex = 1; Pump();
            Check(File.ReadAllText(store.Resolve("01-主项目", "main.ecs")).Contains("项目一已保存"), "project switch preserves edited script");
            Check(Control<Panel>(main, "PhrasesPanel").Children.OfType<Button>().Single().Content?.ToString() == "另一个短语", "project switch changes scoped phrases");
            Check(!editor.Text.Contains("项目一已保存"), "second project editor remains isolated");
            projects.SelectedIndex = 0; Pump();
            Check(editor.Text.Contains("项目一已保存"), "switching back restores saved script");

            var settingsBefore = store.LoadSettings();
            OpenSettings(main, dialog =>
            {
                Field(dialog, "ESP32-S3 · IP / 主机名").Text = "discarded.invalid";
                Descendants<CheckBox>(dialog).First(c => c.Content?.ToString()?.StartsWith("显示手动控制栏") == true).IsChecked = true;
                Button(dialog, "取消").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });
            Check(store.LoadSettings().EspHost == settingsBefore.EspHost && Control<FrameworkElement>(main, "ManualPanel").Visibility == Visibility.Collapsed, "settings cancel leaves disk and interface unchanged");
            OpenSettings(main, dialog =>
            {
                Field(dialog, "ESP32-S3 · IP / 主机名").Text = "192.168.1.88"; Field(dialog, "ESP32 端口").Text = "8080";
                Field(dialog, "手指机器人 · IP / 主机名").Text = "192.168.1.89"; Field(dialog, "机器人端口").Text = "8081";
                Field(dialog, "HTTP 请求超时（ms）").Text = "3500";
                Descendants<CheckBox>(dialog).First(c => c.Content?.ToString()?.StartsWith("显示手动控制栏") == true).IsChecked = true;
                Button(dialog, "保存设置").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });
            var saved = store.LoadSettings();
            Check(saved.ShowManualControls && saved.EspHost == "192.168.1.88" && saved.EspPort == 8080 && saved.RobotHost == "192.168.1.89" && saved.RobotPort == 8081 && saved.RequestTimeoutMs == 3500, "settings save persists device addresses and display preference");
            Check(Control<FrameworkElement>(main, "ManualPanel").Visibility == Visibility.Visible, "saved manual setting applies to main window");
            Check(Control<TextBlock>(main, "RobotAddress").Text.Contains("192.168.1.89:8081"), "HTTP tab uses saved global robot address");
            projects.SelectedIndex = 1; Pump();
            Check(Control<FrameworkElement>(main, "ManualPanel").Visibility == Visibility.Visible && store.LoadSettings().EspHost == "192.168.1.88", "settings retained across project switch");
            projects.SelectedIndex = 0; Pump();
            OpenSettings(main, dialog =>
            {
                Check(Field(dialog, "ESP32-S3 · IP / 主机名").Text == "192.168.1.88", "settings reopen with saved values");
                Descendants<CheckBox>(dialog).First(c => c.Content?.ToString()?.StartsWith("显示手动控制栏") == true).IsChecked = false;
                Capture(dialog, Path.Combine(output, "settings.png"));
                Button(dialog, "保存设置").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });
            Check(Control<FrameworkElement>(main, "ManualPanel").Visibility == Visibility.Collapsed, "manual panel can be hidden again");

            var frame = SyntheticFrame();
            typeof(MainWindow).GetField("_frame", HiddenInstance)!.SetValue(main, frame);
            Control<ImageRegionView>(main, "CaptureView").SetFrame(frame);
            Control<ImageRegionView>(main, "CaptureView").Region = new() { X = 160, Y = 100, Width = 320, Height = 160 };
            main.UpdateLayout(); Pump(); Capture(main, Path.Combine(output, "main-1280x900.png"));
            Check(File.Exists(Path.Combine(output, "main-1280x900.png")), "desktop layout rendered with synthetic image only");
            // Force completed cleanup to cover the synchronous Closing-event path.
            var closingCapture = (IAsyncDisposable)typeof(MainWindow).GetField("_capture", HiddenInstance)!.GetValue(main)!;
            var closingBridge = (IAsyncDisposable)typeof(MainWindow).GetField("_bridge", HiddenInstance)!.GetValue(main)!;
            closingCapture.DisposeAsync().AsTask().GetAwaiter().GetResult();
            closingBridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
            main.Close(); Pump();
            Check(!main.IsVisible, "idle window closes after synchronous cleanup without reentrant Close");
            main = null;
            Console.WriteLine($"UI checks passed: {_checks}"); Console.WriteLine("Screenshots: " + output);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (main != null)
            {
                // This harness never enters Application.Run. Dispose fixture resources directly
                // instead of exercising the production async close/message-loop lifecycle.
                var capture = (IAsyncDisposable)typeof(MainWindow).GetField("_capture", HiddenInstance)!.GetValue(main)!;
                var bridge = (IAsyncDisposable)typeof(MainWindow).GetField("_bridge", HiddenInstance)!.GetValue(main)!;
                capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
                bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
                typeof(MainWindow).GetField("_closed", HiddenInstance)!.SetValue(main, true);
                main.Close();
            }
            app?.Shutdown();
        }
    }

    private static void OpenSettings(MainWindow main, Action<Window> action)
    {
        _automationError = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().First(w => w != main && w.Title == "设置");
            try { dialog.Left = -20000; dialog.Top = -20000; dialog.ShowActivated = false; dialog.UpdateLayout(); action(dialog); }
            catch (Exception error) { _automationError = error; dialog.Close(); }
        });
        typeof(MainWindow).GetMethod("SettingsClick", HiddenInstance)!.Invoke(main, [main, new RoutedEventArgs()]);
        Pump(); if (_automationError != null) throw _automationError;
    }

    private static T Control<T>(MainWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
    private static Button Button(Window dialog, string content) => Descendants<Button>(dialog).First(b => b.Content?.ToString() == content);
    private static TextBox Field(Window window, string label)
    {
        foreach (var panel in Descendants<Panel>(window))
            for (int i = 0; i + 1 < panel.Children.Count; i++)
                if (panel.Children[i] is TextBlock { Text: var text } && text == label && panel.Children[i + 1] is TextBox box) return box;
        throw new InvalidOperationException("Missing field: " + label);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS: " + name); _checks++;
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static FrameSnapshot SyntheticFrame()
    {
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 243, 247)), null, new Rect(0, 0, 640, 360));
            draw.DrawRoundedRectangle(Brushes.White, new Pen(Brushes.SteelBlue, 2), new Rect(160, 100, 320, 160), 12, 12);
            draw.DrawText(new FormattedText("READY", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 32, Brushes.DarkSlateGray, 1), new Point(265, 160));
        }
        var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream);
        return new(stream.ToArray(), 640, 360, DateTimeOffset.Now);
    }
}
