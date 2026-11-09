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
    public ResourceEditorWindow(Window owner, StudioProject project, bool phrases)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Result = ProjectStore.Clone(project); Owner = owner; Title = phrases ? "管理项目短语 / 函数" : "管理项目启动脚本 / 函数"; Width = 980; Height = 680; WindowStartupLocation = WindowStartupLocation.CenterOwner; Topmost = owner.Topmost; ShowInTaskbar = false;
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var hint = new TextBlock { Text = "当前项目：" + project.Name + " · 函数名用于 CALL 调用", Margin = new(0, 0, 0, 16) }; DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var buttons = new WrapPanel { Margin = new(0, 20, 0, 0) }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var textEditor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 150, Margin = new(0, 16, 0, 0) }; DockPanel.SetDock(textEditor, Dock.Bottom); if (phrases) root.Children.Add(textEditor);
        var data = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column };
        void Column(string title, string path, DataGridLength width) => data.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width });
        Column("按钮名称", "Name", new(180)); Column("函数名", "FunctionName", new(180));
        var phraseItems = new ObservableCollection<Phrase>(Result.Phrases); var scriptItems = new ObservableCollection<ScriptShortcut>(Result.Shortcuts);
        if (phrases)
        {
            Column("短语内容", "Text", new(1, DataGridLengthUnitType.Star)); data.ItemsSource = phraseItems;
            textEditor.SetBinding(TextBox.TextProperty, new Binding("SelectedItem.Text") { Source = data, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        }
        else
        {
            Column("脚本路径", "Path", new(1, DataGridLengthUnitType.Star)); Column("参数", "Arguments", new(120)); Column("工作目录", "WorkingDirectory", new(140));
            data.Columns.Add(new DataGridCheckBoxColumn { Header = "等待结束", Binding = new Binding("WaitForExit"), Width = new(80) }); data.ItemsSource = scriptItems;
        }
        root.Children.Add(data);
        Dialogs.Button(buttons, "新增", () => { var suffix = Guid.NewGuid().ToString("N")[..6]; if (phrases) { var item = new Phrase { Name = "新短语_" + suffix, FunctionName = "phrase_" + suffix }; phraseItems.Add(item); data.SelectedItem = item; } else { var item = new ScriptShortcut { Name = "新脚本_" + suffix, FunctionName = "start_" + suffix }; scriptItems.Add(item); data.SelectedItem = item; } });
        Dialogs.Button(buttons, "删除选中", () => { if (data.SelectedItem is Phrase p) phraseItems.Remove(p); if (data.SelectedItem is ScriptShortcut s) scriptItems.Remove(s); });
        if (!phrases) Dialogs.Button(buttons, "选择脚本文件", () => { if (data.SelectedItem is not ScriptShortcut s) return; var picker = new OpenFileDialog { Filter = "启动脚本|*.cmd;*.bat;*.ps1;*.exe" }; if (picker.ShowDialog(this) == true) { s.Path = picker.FileName; data.Items.Refresh(); } });
        Dialogs.Button(buttons, "取消", () => DialogResult = false);
        Dialogs.Button(buttons, "保存到当前项目", () => { try { data.CommitEdit(DataGridEditingUnit.Cell, true); data.CommitEdit(DataGridEditingUnit.Row, true); Result.Phrases = phraseItems.ToList(); Result.Shortcuts = scriptItems.ToList(); ProjectStore.ValidateFunctions(Result); DialogResult = true; } catch (Exception error) { Dialogs.Error(this, error); } }, true);
    }
}
