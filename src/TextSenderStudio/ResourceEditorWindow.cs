using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal sealed class ResourceEditorWindow : Window
{
    public StudioProject Result { get; }

    public ResourceEditorWindow(Window owner, StudioProject project, bool phrases, string? selectedFunction = null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Result = ProjectStore.Clone(project); Owner = owner;
        Title = phrases ? "管理项目短语 / 函数" : "管理项目启动脚本 / 函数";
        Width = 980; Height = 680; MinWidth = 800; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = owner.Topmost; ShowInTaskbar = false;

        // Refactoring UI 2.1 / 3.3: selected-resource details get readable widths; save stays outside scrolling.
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 24) };
        heading.Children.Add(new TextBlock { Text = phrases ? "项目短语" : "项目启动脚本", FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "当前项目：" + project.Name + " · 在主脚本中使用 CALL 函数名调用", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 8, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 24, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(360) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); grid.ColumnDefinitions.Add(new()); root.Children.Add(grid);

        var left = new DockPanel(); grid.Children.Add(left);
        var toolbar = new WrapPanel { Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar);
        var data = new DataGrid { Name = "ResourceRows", AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column };
        data.Columns.Add(new DataGridTextColumn { Header = "按钮名称", Binding = new Binding("Name") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) });
        data.Columns.Add(new DataGridTextColumn { Header = "函数名", Binding = new Binding("FunctionName") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new(1, DataGridLengthUnitType.Star) });
        left.Children.Add(data);
        var phraseItems = new ObservableCollection<Phrase>(Result.Phrases);
        var scriptItems = new ObservableCollection<ScriptShortcut>(Result.Shortcuts);
        if (phrases) data.ItemsSource = phraseItems; else data.ItemsSource = scriptItems;

        var detailCard = new Border { Style = (Style)Application.Current.FindResource("Panel") }; Grid.SetColumn(detailCard, 2); grid.Children.Add(detailCard);
        var detail = new StackPanel(); detailCard.Child = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        detail.Children.Add(new TextBlock { Text = phrases ? "短语内容" : "启动配置", Style = (Style)Application.Current.FindResource("SectionTitle") });
        detail.Children.Add(new TextBlock { Text = phrases ? "点击左侧条目编辑。名称与函数名可在列表内双击修改。" : "点击左侧条目编辑。留空工作目录时使用脚本所在目录。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 0, 0, 16) });
        var fields = new StackPanel(); detail.Children.Add(fields);
        FrameworkElement focusField;
        TextBox BoundField(string label, string property, bool multiline = false)
        {
            var field = Dialogs.Field(fields, label, "", multiline);
            field.SetBinding(TextBox.TextProperty, new Binding("SelectedItem." + property) { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, TargetNullValue = "" });
            return field;
        }
        if (phrases)
        {
            var text = BoundField("发送的文字", "Text", true); text.Height = 240; text.VerticalContentAlignment = VerticalAlignment.Top; focusField = text;
            fields.Children.Add(new TextBlock { Text = "换行会按原样发送；是否在末尾自动回车由主标签中的选项控制。", Style = (Style)Application.Current.FindResource("Caption") });
        }
        else
        {
            var path = BoundField("脚本文件", "Path"); focusField = path;
            var pickerButtons = new WrapPanel { Margin = new(0, 0, 0, 16) }; fields.Children.Add(pickerButtons);
            Dialogs.Button(pickerButtons, "选择脚本文件", () =>
            {
                if (data.SelectedItem is not ScriptShortcut s) return;
                var picker = new OpenFileDialog { Filter = "启动脚本|*.cmd;*.bat;*.ps1;*.exe" };
                if (picker.ShowDialog(this) == true) { s.Path = picker.FileName; path.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); }
            });
            BoundField("参数", "Arguments"); BoundField("工作目录（可留空）", "WorkingDirectory");
            var wait = new CheckBox { Content = "等待脚本结束后继续执行" };
            wait.SetBinding(CheckBox.IsCheckedProperty, new Binding("SelectedItem.WaitForExit") { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); fields.Children.Add(wait);
        }
        fields.IsEnabled = false;
        var empty = new TextBlock { Text = "先新增或选择一个条目。", Style = (Style)Application.Current.FindResource("Caption"), Margin = new(0, 16, 0, 0) }; detail.Children.Add(empty);
        var delete = Dialogs.Button(toolbar, "删除选中", () =>
        {
            data.CancelEdit();
            int index = data.SelectedIndex;
            if (data.SelectedItem is Phrase p) phraseItems.Remove(p);
            else if (data.SelectedItem is ScriptShortcut s) scriptItems.Remove(s);
            data.SelectedIndex = Math.Min(index, data.Items.Count - 1);
        });
        delete.IsEnabled = false;
        var add = Dialogs.Button(toolbar, "＋ 新增", () =>
        {
            if (!data.CommitEdit(DataGridEditingUnit.Cell, true) || !data.CommitEdit(DataGridEditingUnit.Row, true)) return;
            var suffix = Guid.NewGuid().ToString("N")[..6];
            if (phrases) { var item = new Phrase { Name = "新短语_" + suffix, FunctionName = "phrase_" + suffix }; phraseItems.Add(item); data.SelectedItem = item; }
            else { var item = new ScriptShortcut { Name = "新脚本_" + suffix, FunctionName = "start_" + suffix }; scriptItems.Add(item); data.SelectedItem = item; }
            data.ScrollIntoView(data.SelectedItem); focusField.Focus();
        });
        toolbar.Children.Remove(add); toolbar.Children.Insert(0, add);
        data.SelectionChanged += (_, _) => { fields.IsEnabled = delete.IsEnabled = data.SelectedItem != null; empty.Visibility = data.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed; };
        var cancel = Dialogs.Button(footer, "取消", () => DialogResult = false); cancel.IsCancel = true;
        Dialogs.Button(footer, "保存到当前项目", () =>
        {
            try
            {
                if (!data.CommitEdit(DataGridEditingUnit.Cell, true) || !data.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("请先修正正在编辑的内容。");
                Result.Phrases = phraseItems.ToList(); Result.Shortcuts = scriptItems.ToList(); ProjectStore.ValidateFunctions(Result); DialogResult = true;
            }
            catch (Exception error) { Dialogs.Error(this, error); }
        }, true);
        if (phrases) data.SelectedItem = phraseItems.FirstOrDefault(p => p.FunctionName == selectedFunction); else data.SelectedItem = scriptItems.FirstOrDefault(s => s.FunctionName == selectedFunction);
        if (data.SelectedItem == null && data.Items.Count > 0) data.SelectedIndex = 0;
        if (selectedFunction != null) Loaded += (_, _) => { if (data.SelectedItem != null) data.ScrollIntoView(data.SelectedItem); focusField.Focus(); };
    }
}
