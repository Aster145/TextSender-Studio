using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

namespace TextSenderStudio;

internal static class Dialogs
{
    public static Window Window(Window owner, string title, double width = 520) => new() { Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = Math.Max(600, SystemParameters.WorkArea.Height - 80), WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize, Topmost = owner.Topmost };
    public static TextBox Field(Panel parent, string label, string value, bool multiline = false)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new(0, 0, 0, 4) });
        var field = new TextBox { Text = value, Margin = new(0, 0, 0, 16), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (multiline) field.Height = 110; parent.Children.Add(field); return field;
    }
    public static Button Button(Panel panel, string title, Action action, bool primary = false)
    {
        var button = new Button { Content = title }; if (primary) button.Style = (Style)Application.Current.FindResource("PrimaryButton"); button.Click += (_, _) => action(); panel.Children.Add(button); return button;
    }
    public static string? Prompt(Window owner, string title, string label, string value)
    {
        var dialog = Window(owner, title); var panel = new StackPanel { Margin = new(24) }; dialog.Content = panel;
        var input = Field(panel, label, value); var buttons = new WrapPanel(); panel.Children.Add(buttons);
        Button(buttons, "取消", () => dialog.DialogResult = false); Button(buttons, "确定", () => dialog.DialogResult = true, true);
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    public static PixelRegion? PickRegion(Window owner, FrameSnapshot frame)
    {
        var dialog = Window(owner, "框选目标图片", 860); var panel = new StackPanel { Margin = new(24) }; dialog.Content = panel;
        panel.Children.Add(new TextBlock { Text = "拖动框选标签的目标图片。搜索范围使用主标签中设置的识图区域。", Margin = new(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap });
        var view = new ImageRegionView { Height = 420, SelectionEnabled = true }; view.SetFrame(frame); panel.Children.Add(view); view.RegionChanged += (_, _) => view.SelectionEnabled = true;
        var buttons = new WrapPanel { Margin = new(0, 16, 0, 0) }; panel.Children.Add(buttons);
        Button(buttons, "取消", () => dialog.DialogResult = false); Button(buttons, "使用选区", () => { if (view.Region != null) dialog.DialogResult = true; else MessageBox.Show(dialog, "请先框选目标图片。"); }, true);
        return dialog.ShowDialog() == true ? view.Region : null;
    }
    public static void Error(Window owner, Exception error) => MessageBox.Show(owner, error.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
}

internal sealed class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }
    public SettingsWindow(Window owner, AppSettings source, Func<AppSettings, string, string, Task> configureWifi)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Result = ProjectStore.Clone(source); Title = "设置"; Owner = owner; Width = 620; Height = 790; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Topmost = owner.Topmost;
        var panel = new StackPanel { Margin = new(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "全局设置 · 切换项目时保持不变", Margin = new(0, 0, 0, 24) });
        var manual = new CheckBox { Content = "显示手动控制栏（Pro 手柄 / 键盘与文字）", IsChecked = source.ShowManualControls }; panel.Children.Add(manual);
        var onTop = new CheckBox { Content = "默认窗口置顶", IsChecked = source.AlwaysOnTop, Margin = new(0, 8, 0, 20) }; panel.Children.Add(onTop);
        var esp = Dialogs.Field(panel, "ESP32-S3 · IP / 主机名", source.EspHost); var espPort = Dialogs.Field(panel, "ESP32 端口", source.EspPort.ToString());
        var wifi = new WrapPanel { Margin = new(0, 0, 0, 24) }; panel.Children.Add(wifi);
        var wifiButton = new Button { Content = "首次 Wi-Fi 配网" }; wifi.Children.Add(wifiButton);
        wifiButton.Click += async (_, _) =>
        {
            var dialog = Dialogs.Window(this, "首次 Wi-Fi 配网"); var fields = new StackPanel { Margin = new(24) }; dialog.Content = fields;
            fields.Children.Add(new TextBlock { Text = "先让电脑连接开发板热点，然后填写开发板需要加入的 Wi-Fi。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) });
            var ssid = Dialogs.Field(fields, "Wi-Fi 名称", ""); fields.Children.Add(new TextBlock { Text = "Wi-Fi 密码" }); var password = new PasswordBox { Padding = new(8), Margin = new(0, 4, 0, 16) }; fields.Children.Add(password);
            var buttons = new WrapPanel(); fields.Children.Add(buttons); Dialogs.Button(buttons, "取消", () => dialog.DialogResult = false); Dialogs.Button(buttons, "保存到开发板", () => dialog.DialogResult = true, true);
            if (dialog.ShowDialog() != true) return;
            try { wifiButton.IsEnabled = false; var config = ProjectStore.Clone(source); config.EspHost = esp.Text.Trim(); config.EspPort = int.Parse(espPort.Text); ProjectStore.ValidateSettings(config); await configureWifi(config, ssid.Text, password.Password); MessageBox.Show(this, "配置已发送。电脑切回同一 Wi-Fi 后，在设置中填写开发板的新 IP。", "配网"); }
            catch (Exception error) { Dialogs.Error(this, error); }
            finally { password.Clear(); wifiButton.IsEnabled = true; }
        };
        var robot = Dialogs.Field(panel, "手指机器人 · IP / 主机名", source.RobotHost); var robotPort = Dialogs.Field(panel, "机器人端口", source.RobotPort.ToString());
        panel.Children.Add(new TextBlock { Text = "协议", Margin = new(0, 0, 0, 4) }); var protocol = new ComboBox { ItemsSource = new[] { "http", "https" }, SelectedItem = source.RobotScheme, Margin = new(0, 0, 0, 16) }; panel.Children.Add(protocol);
        var timeout = Dialogs.Field(panel, "HTTP 请求超时（ms）", source.RequestTimeoutMs.ToString());
        var lanFields = new StackPanel { Margin = new(8, 16, 8, 8) }; var lan = new CheckBox { Content = "启用局域网接口（免验证）", IsChecked = source.LanEnabled }; lanFields.Children.Add(lan);
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(LanControlServer.IsLocalAddress).Select(a => a.ToString()).Append("127.0.0.1").Append(source.LanBindAddress).Distinct().ToArray();
        lanFields.Children.Add(new TextBlock { Text = "监听 IP（供局域网访问时请选择电脑的局域网 IP）", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 4) });
        var address = new ComboBox { ItemsSource = addresses, SelectedItem = source.LanBindAddress, Margin = new(0, 0, 0, 12) }; lanFields.Children.Add(address);
        var lanPort = Dialogs.Field(lanFields, "本机接口端口", source.LanPort.ToString()); panel.Children.Add(new Expander { Header = "本机局域网接口", Content = lanFields, Margin = new(0, 8, 0, 20) });
        panel.Children.Add(new TextBlock { Text = "保存在程序目录的 settings.json；Wi-Fi 密码仅写入开发板。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 20), FontSize = 12 });
        var actions = new WrapPanel(); panel.Children.Add(actions); Dialogs.Button(actions, "取消", () => DialogResult = false);
        Dialogs.Button(actions, "保存设置", () =>
        {
            try { Result.ShowManualControls = manual.IsChecked == true; Result.AlwaysOnTop = onTop.IsChecked == true; Result.EspHost = esp.Text.Trim(); Result.EspPort = int.Parse(espPort.Text); Result.RobotHost = robot.Text.Trim(); Result.RobotPort = int.Parse(robotPort.Text); Result.RobotScheme = (string)protocol.SelectedItem; Result.RequestTimeoutMs = int.Parse(timeout.Text); Result.LanEnabled = lan.IsChecked == true; Result.LanPort = int.Parse(lanPort.Text); Result.LanBindAddress = (string)address.SelectedItem; ProjectStore.ValidateSettings(Result); DialogResult = true; }
            catch (Exception error) { Dialogs.Error(this, error); }
        }, true);
    }
}
