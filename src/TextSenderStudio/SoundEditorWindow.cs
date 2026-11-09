using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal sealed class SoundEditorWindow : Window
{
    public StudioProject Result { get; }
    private sealed record Choice(string Id, string Label);

    public SoundEditorWindow(Window owner, StudioProject project, string? selectedFunction = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window)); Owner = owner;
        Result = ProjectStore.Clone(project); Title = "项目声音提醒"; Width = 960; Height = 620; MinWidth = 800; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Topmost = owner.Topmost;
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 24) }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = "声音提醒", FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "当前项目：" + project.Name + " · 使用 CALL 函数名播放提醒", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 8, 0, 0) });
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 24, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(360) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); grid.ColumnDefinitions.Add(new()); root.Children.Add(grid);
        var left = new DockPanel(); grid.Children.Add(left);
        var toolbar = new WrapPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar);
        var sounds = new ObservableCollection<SoundAction>(Result.Sounds);
        var data = new DataGrid { Name = "SoundRows", ItemsSource = sounds, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column };
        data.Columns.Add(new DataGridTextColumn { Header = "提醒名称", Binding = new Binding("Name") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) });
        data.Columns.Add(new DataGridTextColumn { Header = "函数名", Binding = new Binding("FunctionName") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) });
        left.Children.Add(data);
        var card = new Border { Style = (Style)Application.Current.FindResource("Panel") }; Grid.SetColumn(card, 2); grid.Children.Add(card);
        var fields = new StackPanel(); card.Child = new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        fields.Children.Add(new TextBlock { Text = "播放内容", Style = (Style)Application.Current.FindResource("SectionTitle") });
        fields.Children.Add(new TextBlock { Text = "声音来源", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 4) });
        var source = new ComboBox { Name = "SoundSource", ItemsSource = new[] { new Choice("System", "系统提示音"), new Choice("File", "自选音乐文件") }, DisplayMemberPath = "Label", SelectedValuePath = "Id", Margin = new(0, 0, 0, 16) };
        source.SetBinding(ComboBox.SelectedValueProperty, new Binding("SelectedItem.Source") { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); fields.Children.Add(source);
        var presetFields = new StackPanel(); fields.Children.Add(presetFields);
        presetFields.Children.Add(new TextBlock { Text = "系统声音", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 4) });
        var preset = new ComboBox { Name = "SoundPreset", ItemsSource = new[] { new Choice("Asterisk", "提示"), new Choice("Beep", "蜂鸣"), new Choice("Exclamation", "注意"), new Choice("Hand", "错误"), new Choice("Question", "询问") }, DisplayMemberPath = "Label", SelectedValuePath = "Id", Margin = new(0, 0, 0, 16) };
        preset.SetBinding(ComboBox.SelectedValueProperty, new Binding("SelectedItem.SystemSound") { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); presetFields.Children.Add(preset);
        var fileFields = new StackPanel(); fields.Children.Add(fileFields);
        var path = Dialogs.Field(fileFields, "音乐文件", ""); path.Name = "SoundFile";
        path.SetBinding(TextBox.TextProperty, new Binding("SelectedItem.FilePath") { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, TargetNullValue = "" });
        var pickerRow = new WrapPanel { Margin = new(0, 0, 0, 16) }; fileFields.Children.Add(pickerRow);
        Dialogs.Button(pickerRow, "选择音乐文件", () =>
        {
            if (data.SelectedItem is not SoundAction sound) return;
            var picker = new OpenFileDialog { Filter = "音乐文件|*.mp3;*.wav;*.wma|所有文件|*.*" };
            if (picker.ShowDialog(this) == true) { sound.FilePath = picker.FileName; path.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); }
        });
        fields.Children.Add(new TextBlock { Text = "系统提示音使用 Windows 当前声音方案。自选文件保留在原位置，运行时需能访问该文件。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 8, 0, 0) });
        var call = new TextBlock { Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 24, 0, 0) }; fields.Children.Add(call);
        Button? delete = null;
        void UpdateSelection()
        {
            fields.IsEnabled = data.SelectedItem != null;
            if (delete != null) delete.IsEnabled = data.SelectedItem != null;
            var sound = data.SelectedItem as SoundAction;
            presetFields.Visibility = sound?.Source == "File" ? Visibility.Collapsed : Visibility.Visible;
            fileFields.Visibility = sound?.Source == "File" ? Visibility.Visible : Visibility.Collapsed;
            call.Text = sound == null ? "先新增或选择一个声音提醒。" : "脚本调用：CALL " + sound.FunctionName;
        }
        source.SelectionChanged += (_, _) => Dispatcher.BeginInvoke((Action)UpdateSelection);
        data.SelectionChanged += (_, _) => UpdateSelection();
        data.CellEditEnding += (_, _) => Dispatcher.BeginInvoke((Action)UpdateSelection);
        Dialogs.Button(toolbar, "＋ 新增", () =>
        {
            if (!data.CommitEdit(DataGridEditingUnit.Cell, true) || !data.CommitEdit(DataGridEditingUnit.Row, true)) return;
            var suffix = Guid.NewGuid().ToString("N")[..6];
            var sound = new SoundAction { Name = "声音提醒_" + suffix, FunctionName = "sound_" + suffix };
            sounds.Add(sound); data.SelectedItem = sound; data.ScrollIntoView(sound);
        });
        delete = Dialogs.Button(toolbar, "删除选中", () =>
        {
            data.CancelEdit(); int index = data.SelectedIndex;
            if (data.SelectedItem is SoundAction sound) sounds.Remove(sound);
            data.SelectedIndex = Math.Min(index, data.Items.Count - 1); UpdateSelection();
        });
        var cancel = Dialogs.Button(footer, "取消", () => DialogResult = false); cancel.IsCancel = true;
        Dialogs.Button(footer, "保存到当前项目", () =>
        {
            try
            {
                if (!data.CommitEdit(DataGridEditingUnit.Cell, true) || !data.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("请先修正正在编辑的内容。");
                foreach (var sound in sounds)
                {
                    if (string.IsNullOrWhiteSpace(sound.Name)) throw new ArgumentException("请填写声音提醒名称。");
                    if (sound.Source == "File" && string.IsNullOrWhiteSpace(sound.FilePath)) throw new ArgumentException("请选择要播放的音乐文件。");
                }
                Result.Sounds = sounds.ToList(); ProjectStore.ValidateFunctions(Result); DialogResult = true;
            }
            catch (Exception error) { Dialogs.Error(this, error); }
        }, true);
        data.SelectedItem = sounds.FirstOrDefault(s => s.FunctionName == selectedFunction);
        if (data.SelectedItem == null && sounds.Count > 0) data.SelectedIndex = 0;
        UpdateSelection();
        if (selectedFunction != null) Loaded += (_, _) => { if (data.SelectedItem != null) data.ScrollIntoView(data.SelectedItem); source.Focus(); };
    }
}
