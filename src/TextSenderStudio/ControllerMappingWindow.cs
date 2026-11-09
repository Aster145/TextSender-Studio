using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal sealed class ControllerMappingWindow : Window
{
    public DirectControlSettings Result { get; }
    private readonly ObservableCollection<MappingRow> _rows;

    public ControllerMappingWindow(Window owner, DirectControlSettings settings)
    {
        Owner = owner; Title = "键盘映射"; Width = 640; Height = 720; MinWidth = 520; MinHeight = 520;
        Style = (Style)Application.Current.FindResource(typeof(Window)); Topmost = owner.Topmost;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Result = ProjectStore.Clone(settings);
        _rows = new(ControllerCatalog.Targets.Select(target => new MappingRow(target,
            Result.KeyBindings.TryGetValue(target.Name, out var code) ? code : 0)));
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var heading = new StackPanel { Margin = new(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = "键盘映射", FontSize = 20, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "选择每个手柄动作对应的键盘按键。允许同一按键控制多个动作；未绑定的动作仍可用鼠标操作。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 0, 0) };
        var reset = new Button { Content = "恢复 EasyCon 默认" }; reset.Click += (_, _) =>
        {
            var defaults = ControllerCatalog.DefaultBindings(); foreach (var row in _rows) row.KeyCode = defaults[row.Target];
        };
        buttons.Children.Add(reset);
        var cancel = new Button { Content = "取消", IsCancel = true }; cancel.Click += (_, _) => DialogResult = false; buttons.Children.Add(cancel);
        var save = new Button { Content = "保存映射", Style = (Style)Application.Current.FindResource("PrimaryButton") };
        save.Click += (_, _) => { Result.KeyBindings = _rows.ToDictionary(row => row.Target, row => row.KeyCode, StringComparer.OrdinalIgnoreCase); DialogResult = true; }; buttons.Children.Add(save);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var grid = new DataGrid { ItemsSource = _rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 44, SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = "分组", Binding = new Binding(nameof(MappingRow.Group)), IsReadOnly = true, Width = 88 });
        grid.Columns.Add(new DataGridTextColumn { Header = "手柄动作", Binding = new Binding(nameof(MappingRow.Label)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var chooser = new FrameworkElementFactory(typeof(ComboBox));
        chooser.SetValue(ItemsControl.ItemsSourceProperty, KeyChoices()); chooser.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(KeyChoice.Label)); chooser.SetValue(System.Windows.Controls.Primitives.Selector.SelectedValuePathProperty, nameof(KeyChoice.Code));
        chooser.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, new Binding(nameof(MappingRow.KeyCode)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        chooser.SetValue(MarginProperty, new Thickness(4, 2, 4, 2));
        grid.Columns.Add(new DataGridTemplateColumn { Header = "键盘按键", CellTemplate = new DataTemplate { VisualTree = chooser }, Width = 184 });
        root.Children.Add(grid);
    }

    private static KeyChoice[] KeyChoices()
    {
        var keys = new List<KeyChoice> { new(0, "未绑定") };
        for (int code = 8; code < 255; code++)
        {
            if (code is 16 or 17 or 18) continue; // Captured modifiers use their left/right VK codes.
            Key key = KeyInterop.KeyFromVirtualKey(code);
            if (key is Key.None or Key.System or Key.DeadCharProcessed) continue;
            keys.Add(new(code, key.ToString()));
        }
        return keys.ToArray();
    }

    private sealed record KeyChoice(int Code, string Label);
    private sealed class MappingRow(ControllerTarget target, int keyCode) : INotifyPropertyChanged
    {
        public string Target => target.Name;
        public string Group => target.Group;
        public string Label => target.Name switch
        {
            "CAPTURE" => "截图 · CAPTURE",
            _ when target.Name.StartsWith("LS_") || target.Name.StartsWith("RS_") => target.Name.Split('_', 2)[1] switch
            { "RESET" => "回中", "UP" => "↑ 上", "UPRIGHT" => "↗ 右上", "RIGHT" => "→ 右", "DOWNRIGHT" => "↘ 右下", "DOWN" => "↓ 下", "DOWNLEFT" => "↙ 左下", "LEFT" => "← 左", "UPLEFT" => "↖ 左上", _ => target.Label },
            _ => target.Label
        };
        public int KeyCode { get => keyCode; set { if (keyCode == value) return; keyCode = value; PropertyChanged?.Invoke(this, new(nameof(KeyCode))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
