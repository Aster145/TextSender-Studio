using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using TextSenderStudio.Core;
using TextSenderStudio.Vision;

namespace TextSenderStudio;

internal static class Dialogs
{
    public static Window Window(Window owner, string title, double width = 520) => new() { Style = (Style)Application.Current.FindResource(typeof(Window)), Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height, MaxHeight = SystemParameters.WorkArea.Height - 48, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, ResizeMode = ResizeMode.CanResize, Topmost = owner.Topmost };
    public static TextBox Field(Panel parent, string label, string value, bool multiline = false)
    {
        parent.Children.Add(new TextBlock { Text = label, Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 4) });
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
        : this(owner, source, configureWifi, null, false) { }

    public SettingsWindow(Window owner, AppSettings source, Func<AppSettings, string, string, Task> configureWifi, DiagnosticLogBuffer? diagnostics, bool logsOnly)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Result = ProjectStore.Clone(source); Title = "设置"; Owner = owner; Width = 760; Height = 820; MinWidth = 620; MinHeight = 560; MaxHeight = SystemParameters.WorkArea.Height - 48; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Topmost = owner.Topmost;
        // Refactoring UI §2.1 / §3.6: group related fields; keep the primary save action visible.
        var root = new DockPanel(); Content = root;
        var heading = new StackPanel { Margin = new(24, 24, 24, 16) };
        heading.Children.Add(new TextBlock { Text = "设置", FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "设备地址与界面偏好在所有项目之间共用。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 8, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new Border { BorderThickness = new(0, 1, 0, 0), Padding = new(24, 16, 24, 16) };
        footer.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); footer.SetResourceReference(Border.BorderBrushProperty, "LineBrush");
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Child = actions;
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl { Margin = new(16, 0, 16, 8) }; root.Children.Add(tabs);
        var panel = new StackPanel { Margin = new(8, 8, 8, 0) };
        var general = new TabItem { Header = "常规设置", IsEnabled = !logsOnly, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        tabs.Items.Add(general);
        var logPanel = new DockPanel { Margin = new(8) };
        var logTools = new DockPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(logTools, Dock.Top); logPanel.Children.Add(logTools);
        var logButtons = new WrapPanel(); DockPanel.SetDock(logButtons, Dock.Right); logTools.Children.Add(logButtons);
        var buffer = diagnostics ?? new DiagnosticLogBuffer();
        Dialogs.Button(logButtons, "复制日志", () => { if (buffer.Text.Length > 0) Clipboard.SetText(buffer.Text); });
        Dialogs.Button(logButtons, "清空日志", buffer.Clear);
        logTools.Children.Add(new TextBlock { Text = logsOnly ? "运行中 · 可实时查看日志，设置暂时锁定" : "设备通信、执行详情与错误信息", Style = (Style)Application.Current.FindResource("Caption") });
        var logBox = new TextBox { Name = "DiagnosticLogBox", Style = (Style)Application.Current.FindResource("ConsoleLog"), Text = buffer.Text }; logPanel.Children.Add(logBox);
        void RefreshLog() { logBox.Text = buffer.Text; logBox.ScrollToEnd(); }
        buffer.Changed += RefreshLog; Closed += (_, _) => buffer.Changed -= RefreshLog;
        tabs.Items.Add(new TabItem { Header = "执行日志", Content = logPanel }); tabs.SelectedIndex = logsOnly ? 1 : 0;
        var appearance = Section(panel, "界面", "手动控制开关位于主标签；窗口置顶也可在顶部直接切换。");
        var onTop = new CheckBox { Content = "默认窗口置顶", IsChecked = source.AlwaysOnTop, Margin = new(0, 8, 0, 0) }; appearance.Children.Add(onTop);
        var espSection = Section(panel, "ESP32-S3", "填写开发板连接路由器后获得的局域网地址。");
        var (esp, espPort) = AddressFields(espSection, "ESP32-S3 · IP / 主机名", source.EspHost, "ESP32 端口", source.EspPort.ToString());
        var wifi = new WrapPanel(); espSection.Children.Add(wifi);
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
        var robotSection = Section(panel, "默认 HTTP 设备", "相对路径使用此设备；在步骤中直接填写完整 URL 可控制其他设备。");
        var (robot, robotPort) = AddressFields(robotSection, "默认 HTTP 设备 · IP / 主机名", source.RobotHost, "HTTP 默认端口", source.RobotPort.ToString());
        var networkOptions = new Grid(); networkOptions.ColumnDefinitions.Add(new()); networkOptions.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); networkOptions.ColumnDefinitions.Add(new()); robotSection.Children.Add(networkOptions);
        var protocolFields = new StackPanel(); networkOptions.Children.Add(protocolFields);
        protocolFields.Children.Add(new TextBlock { Text = "协议", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 4) }); var protocol = new ComboBox { ItemsSource = new[] { "http", "https" }, SelectedItem = source.RobotScheme, Margin = new(0) }; protocolFields.Children.Add(protocol);
        var timeoutFields = new StackPanel(); Grid.SetColumn(timeoutFields, 2); networkOptions.Children.Add(timeoutFields);
        var timeout = Dialogs.Field(timeoutFields, "HTTP 请求超时（ms）", source.RequestTimeoutMs.ToString()); timeout.Margin = new(0);
        var pushSection = Section(panel, "推送服务", "配置 EasyCon 脚本通知使用的服务与密钥。");
        var pushSummary = new TextBlock { Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 12) }; pushSection.Children.Add(pushSummary);
        void UpdatePushSummary() => pushSummary.Text = "已启用 " + Result.PushNotifications.Channels.Count(c => c.Enabled) + " 个服务";
        UpdatePushSummary();
        var pushButtons = new WrapPanel(); pushSection.Children.Add(pushButtons);
        Dialogs.Button(pushButtons, "配置推送服务", () =>
        {
            var dialog = new PushSettingsWindow(this, Result.PushNotifications);
            if (dialog.ShowDialog() == true) { Result.PushNotifications = dialog.Result; UpdatePushSummary(); }
        });
        var lanFields = new StackPanel { Margin = new(0, 16, 0, 0) }; var lan = new CheckBox { Content = "启用局域网接口（免验证）", IsChecked = source.LanEnabled }; lanFields.Children.Add(lan);
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(LanControlServer.IsLocalAddress).Select(a => a.ToString()).Append("127.0.0.1").Append(source.LanBindAddress).Distinct().ToArray();
        lanFields.Children.Add(new TextBlock { Text = "监听 IP（供局域网访问时请选择电脑的局域网 IP）", TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 4) });
        var address = new ComboBox { ItemsSource = addresses, SelectedItem = source.LanBindAddress, Margin = new(0, 0, 0, 12) }; lanFields.Children.Add(address);
        var lanPort = Dialogs.Field(lanFields, "本机接口端口", source.LanPort.ToString()); lanPort.Width = 160; lanPort.HorizontalAlignment = HorizontalAlignment.Left; lanPort.Margin = new(0);
        var lanCard = new Border { Style = (Style)Application.Current.FindResource("Panel"), Margin = new(0, 0, 0, 16), Child = new Expander { Header = "本机局域网接口", Content = lanFields } }; panel.Children.Add(lanCard);
        panel.Children.Add(new TextBlock { Text = "设置保存在程序目录；Wi-Fi 密码仅写入开发板。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 16) });
        var cancel = Dialogs.Button(actions, logsOnly ? "关闭" : "取消", () => DialogResult = false); cancel.IsCancel = true;
        var save = Dialogs.Button(actions, "保存设置", () =>
        {
            try { Result.AlwaysOnTop = onTop.IsChecked == true; Result.EspHost = esp.Text.Trim(); Result.EspPort = int.Parse(espPort.Text); Result.RobotHost = robot.Text.Trim(); Result.RobotPort = int.Parse(robotPort.Text); Result.RobotScheme = (string)protocol.SelectedItem; Result.RequestTimeoutMs = int.Parse(timeout.Text); Result.LanEnabled = lan.IsChecked == true; Result.LanPort = int.Parse(lanPort.Text); Result.LanBindAddress = (string)address.SelectedItem; ProjectStore.ValidateSettings(Result); DialogResult = true; }
            catch (Exception error) { Dialogs.Error(this, error); }
        }, true);
        save.IsEnabled = !logsOnly;
    }

    private static StackPanel Section(Panel parent, string title, string description)
    {
        var content = new StackPanel();
        var card = new Border { Style = (Style)Application.Current.FindResource("Panel"), Margin = new(0, 0, 0, 16), Child = content }; parent.Children.Add(card);
        content.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = description, Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 4, 0, 16) });
        return content;
    }

    private static (TextBox Host, TextBox Port) AddressFields(Panel parent, string hostLabel, string hostValue, string portLabel, string portValue)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(120) }); parent.Children.Add(row);
        var hostFields = new StackPanel(); row.Children.Add(hostFields); var portFields = new StackPanel(); Grid.SetColumn(portFields, 2); row.Children.Add(portFields);
        return (Dialogs.Field(hostFields, hostLabel, hostValue), Dialogs.Field(portFields, portLabel, portValue));
    }
}
