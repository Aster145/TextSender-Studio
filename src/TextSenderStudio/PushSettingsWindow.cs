using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal sealed class PushSettingsWindow : Window
{
    public PushNotificationSettings Result { get; private set; }
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private sealed class ChannelDraft(PushNotificationChannel channel)
    {
        public PushNotificationChannel Channel { get; } = channel;
        public string Name { get => Channel.Name; set => Channel.Name = value; }
        public bool Enabled { get => Channel.Enabled; set => Channel.Enabled = value; }
        public string HeadersText { get; set; } = JsonSerializer.Serialize(channel.Headers, PrettyJson);
        public string VariablesText { get; set; } = JsonSerializer.Serialize(channel.Variables, PrettyJson);
        public PushNotificationChannel Build()
        {
            var result = ProjectStore.Clone(Channel);
            result.Headers = ReadMap(HeadersText, "请求头"); result.Variables = ReadMap(VariablesText, "自定义变量");
            return result;
        }
        private static Dictionary<string, string> ReadMap(string text, string title)
        {
            if (string.IsNullOrWhiteSpace(text)) return [];
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(text) ?? []; }
            catch (JsonException) { throw new ArgumentException(title + "须为 JSON 对象，键和值均使用字符串。"); }
        }
    }

    public PushSettingsWindow(Window owner, PushNotificationSettings source)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window)); Owner = owner; Result = ProjectStore.Clone(source);
        Title = "推送服务"; Width = 1000; Height = 760; MinWidth = 800; MinHeight = 560; MaxHeight = SystemParameters.WorkArea.Height - 48;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = owner.Topmost; ShowInTaskbar = false;
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 24) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "推送服务", FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "勾选需要使用的服务。脚本触发通知时，会发送到所有已启用的服务。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 8, 0, 0) });
        var footer = new DockPanel { Margin = new(0, 24, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        var timing = new StackPanel { Orientation = Orientation.Horizontal };
        timing.Children.Add(new TextBlock { Text = "超时（ms）", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 8, 0) });
        var timeout = new TextBox { Text = source.TimeoutMs.ToString(), Width = 100 }; timing.Children.Add(timeout); footer.Children.Add(timing);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(260) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); grid.ColumnDefinitions.Add(new()); root.Children.Add(grid);
        var left = new DockPanel(); grid.Children.Add(left);
        var toolbar = new StackPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar);
        var presets = new ComboBox { ItemsSource = PushNotificationTemplates.CreateDefaults(), DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new(0, 0, 0, 8) }; toolbar.Children.Add(presets);
        var rowButtons = new WrapPanel(); toolbar.Children.Add(rowButtons);
        var drafts = new ObservableCollection<ChannelDraft>(Result.Channels.Select(c => new ChannelDraft(c)));
        var data = new DataGrid { Name = "PushChannels", ItemsSource = drafts, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column };
        data.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new Binding("Enabled") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(56) });
        data.Columns.Add(new DataGridTextColumn { Header = "服务名称", Binding = new Binding("Name") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) }); left.Children.Add(data);

        var card = new Border { Style = (Style)Application.Current.FindResource("Panel") }; Grid.SetColumn(card, 2); grid.Children.Add(card);
        var details = new StackPanel(); card.Child = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        details.Children.Add(new TextBlock { Text = "服务配置", Style = (Style)Application.Current.FindResource("SectionTitle") });
        var empty = new TextBlock { Text = "选择一个服务，或从左侧添加预设。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 16) }; details.Children.Add(empty);
        var fields = new StackPanel(); details.Children.Add(fields);
        TextBox BoundField(Panel parent, string title, string path, bool multiline = false)
        {
            var field = Dialogs.Field(parent, title, "", multiline);
            field.SetBinding(TextBox.TextProperty, new Binding("SelectedItem." + path) { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, TargetNullValue = "" });
            return field;
        }
        fields.Children.Add(new TextBlock { Text = "请求方法", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 4) });
        var method = new ComboBox { ItemsSource = new[] { "GET", "POST" }, Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 0, 0, 16) };
        method.SetBinding(ComboBox.SelectedItemProperty, new Binding("SelectedItem.Channel.Method") { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); fields.Children.Add(method);
        BoundField(fields, "服务 URL", "Channel.Url");
        fields.Children.Add(new TextBlock { Text = "Token / 密钥（对应 {{token}}）", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 4) });
        var token = new PasswordBox { Name = "PushToken", Padding = new(12, 8, 12, 8), Margin = new(0, 0, 0, 16) }; fields.Children.Add(token);
        ChannelDraft? selected = null; bool loadingToken = false;
        token.PasswordChanged += (_, _) => { if (!loadingToken && selected != null) selected.Channel.Token = token.Password; };
        BoundField(fields, "请求正文（POST）", "Channel.Body", true);
        fields.Children.Add(new TextBlock { Text = "可使用 {{content}}、{{title}}、{{token}}；附加参数可在下方定义后用 {{变量名}} 引用。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 16) });
        var advanced = new StackPanel { Margin = new(0, 16, 0, 0) };
        BoundField(advanced, "请求头（JSON 对象）", "HeadersText", true);
        BoundField(advanced, "自定义变量（JSON 对象）", "VariablesText", true);
        fields.Children.Add(new Expander { Header = "请求头与自定义变量", Content = advanced });
        Button? delete = null;
        void UpdateSelection()
        {
            selected = data.SelectedItem as ChannelDraft;
            fields.IsEnabled = selected != null;
            if (delete != null) delete.IsEnabled = selected != null;
            empty.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
            loadingToken = true;
            try { token.Password = selected?.Channel.Token ?? ""; }
            finally { loadingToken = false; }
        }
        data.SelectionChanged += (_, _) => UpdateSelection();
        Dialogs.Button(rowButtons, "＋ 添加预设", () =>
        {
            if (!data.CommitEdit(DataGridEditingUnit.Cell, true) || !data.CommitEdit(DataGridEditingUnit.Row, true)) return;
            if (presets.SelectedItem is not PushNotificationChannel template) return;
            var draft = new ChannelDraft(ProjectStore.Clone(template)); draft.Enabled = false; drafts.Add(draft); data.SelectedItem = draft; data.ScrollIntoView(draft);
        });
        delete = Dialogs.Button(rowButtons, "删除", () =>
        {
            data.CancelEdit(); int index = data.SelectedIndex;
            if (data.SelectedItem is ChannelDraft draft) drafts.Remove(draft);
            data.SelectedIndex = Math.Min(index, data.Items.Count - 1); UpdateSelection();
        });
        var cancel = Dialogs.Button(actions, "取消", () => DialogResult = false); cancel.IsCancel = true;
        Dialogs.Button(actions, "保存推送配置", () =>
        {
            try
            {
                if (!data.CommitEdit(DataGridEditingUnit.Cell, true) || !data.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("请先修正正在编辑的内容。");
                if (!int.TryParse(timeout.Text, out int ms)) throw new ArgumentException("请输入整数毫秒。");
                var candidate = ProjectStore.Clone(source); candidate.TimeoutMs = ms; candidate.Channels = drafts.Select(d => d.Build()).ToList();
                PushNotificationDispatcher.Validate(candidate); Result = candidate; DialogResult = true;
            }
            catch (Exception error) { Dialogs.Error(this, error); }
        }, true);
        if (drafts.Count > 0) data.SelectedIndex = 0; UpdateSelection();
        Closed += (_, _) => { loadingToken = true; token.Clear(); };
    }
}
