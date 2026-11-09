using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core;

internal static class ManualControlChecks
{
    public static void Run(MainWindow main, Action<bool, string> check, Action<Window, string> capture)
    {
        var toggle = (CheckBox)main.FindName("ManualControlsCheck");
        var panel = (FrameworkElement)main.FindName("ManualPanel");
        var topmost = (CheckBox)main.FindName("TopmostCheck");
        var controller = (FrameworkElement)main.FindName("ControllerLayout");
        var tabs = (TabControl)main.FindName("MainTabs");
        var store = new ProjectStore(App.DataRoot);
        bool wasManual = toggle.IsChecked == true, wasTopmost = topmost.IsChecked == true;
        int tab = tabs.SelectedIndex;
        double width = main.Width, height = main.Height;
        try
        {
            tabs.SelectedIndex = 0; toggle.IsChecked = false; Pump(main);
            check(toggle.IsVisible && panel.Visibility == Visibility.Collapsed && !store.LoadSettings().ShowManualControls,
                "manual switch stays accessible while controls are hidden and persists off");
            toggle.IsChecked = true; Pump(main);
            check(panel.Visibility == Visibility.Visible && store.LoadSettings().ShowManualControls,
                "manual switch immediately shows controls and persists on");

            var buttons = Descendants<Button>(controller).ToDictionary(button => (string)button.Tag);
            string[] supported = ["UP", "DOWN", "LEFT", "RIGHT", "UP+LEFT", "UP+RIGHT", "DOWN+LEFT", "DOWN+RIGHT", "A", "B", "X", "Y", "L", "R", "ZL", "ZR", "MINUS", "PLUS", "HOME", "CAPTURE", "L3", "R3", "L+R"];
            check(buttons.Keys.Order().SequenceEqual(supported.Order()), "manual controller exposes all supported requested buttons");
            check(buttons.Keys.All(tag => { var state = InputBridge.ParsePad(tag); return state.Buttons != 0 || state.Dpad != 15; }),
                "manual button tags parse locally without issuing device requests");
            Point Center(string name)
            {
                var button = buttons[name];
                return button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), controller);
            }
            check(Center("X").Y < Center("Y").Y && Center("B").Y > Center("A").Y
                && Center("Y").X < Center("X").X && Center("A").X > Center("B").X,
                "Nintendo face buttons place X above, B below, Y left and A right");
            check(Center("UP").Y < Center("LEFT").Y && Center("DOWN").Y > Center("RIGHT").Y
                && Center("LEFT").X < Center("UP").X && Center("RIGHT").X > Center("DOWN").X,
                "direction buttons use a diamond arrangement");
            check(Center("ZL").X < Center("L").X && Center("R").X < Center("ZR").X
                && Center("L").Y < Center("UP").Y && Center("R").Y < Center("X").Y,
                "shoulder buttons sit above their respective controller halves");
            check(((Button)main.FindName("ReleaseAllButton")).IsVisible
                && ((TextBox)main.FindName("KeyDuration")).Text == "100"
                && ((TextBox)main.FindName("ManualText")).IsVisible,
                "release, duration and keyboard text controls remain available");

            foreach (double targetWidth in new[] { 1280d, 1000d })
            {
                main.Width = targetWidth; main.Height = 900;
                panel.BringIntoView(); Pump(main);
                check(buttons.Values.All(button =>
                {
                    var location = button.TranslatePoint(new Point(), panel);
                    return location.X >= 0 && location.X + button.ActualWidth <= panel.ActualWidth;
                }), $"manual buttons fit without horizontal clipping at {targetWidth:0}px");
                capture(main, $"manual-controls-{targetWidth:0}.png");
            }

            topmost.IsChecked = true; Pump(main);
            check(main.Topmost && store.LoadSettings().AlwaysOnTop, "global topmost switch updates window and saved preference");
            for (int index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index; Pump(main);
                check(topmost.IsVisible, $"global topmost switch is visible on primary tab {index + 1}");
            }
            topmost.IsChecked = false;
            check(!main.Topmost && !store.LoadSettings().AlwaysOnTop, "global topmost switch can disable and persist the preference");

            var settingsType = typeof(MainWindow).Assembly.GetType("TextSenderStudio.SettingsWindow")!;
            var source = store.LoadSettings();
            var settings = (Window)Activator.CreateInstance(settingsType, main, source,
                new Func<AppSettings, string, string, Task>((_, _, _) => throw new InvalidOperationException("No Wi-Fi actions in UI checks.")))!;
            var result = (AppSettings)settingsType.GetProperty("Result")!.GetValue(settings)!;
            check(result.ShowManualControls && !LogicalDescendants<CheckBox>(settings).Any(box => box.Content?.ToString()?.StartsWith("显示手动控制栏", StringComparison.Ordinal) == true),
                "settings preserves the manual preference without exposing a duplicate switch");
            settings.Close();
        }
        finally
        {
            toggle.IsChecked = wasManual; topmost.IsChecked = wasTopmost;
            tabs.SelectedIndex = tab; main.Width = width; main.Height = height; Pump(main);
        }
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

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in LogicalDescendants<T>(child)) yield return nested;
        }
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
