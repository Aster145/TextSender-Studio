using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TextSenderStudio;

/// <summary>Compact, selectable help adjacent to the script, without a fourth main tab.</summary>
public sealed class SyntaxHelpView : UserControl
{
    public static readonly DependencyProperty EditorProperty = DependencyProperty.Register(nameof(Editor), typeof(ScriptEditorBox), typeof(SyntaxHelpView));
    public ScriptEditorBox? Editor { get => (ScriptEditorBox?)GetValue(EditorProperty); set => SetValue(EditorProperty, value); }

    public SyntaxHelpView()
    {
        var content = new StackPanel { Margin = new(0, 12, 0, 0) };
        var hint = new TextBlock { Text = "Ctrl+Space 补全 · Enter 自动缩进 · Tab / Shift+Tab 调整缩进 · 双击函数或按 F12 跳转", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        hint.SetResourceReference(StyleProperty, "Caption"); content.Children.Add(hint);
        var choices = new ComboBox { ItemsSource = EditorSyntax.Examples, DisplayMemberPath = nameof(SyntaxExample.Title), SelectedIndex = 0, Margin = new(0, 0, 0, 8), MaxWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
        content.Children.Add(choices);
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }; description.SetResourceReference(StyleProperty, "Caption"); content.Children.Add(description);
        var example = new TextBox { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"), FontSize = 14, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 96, MaxHeight = 192, VerticalContentAlignment = VerticalAlignment.Top };
        content.Children.Add(example);
        var actions = new WrapPanel { Margin = new(0, 8, 0, 0) }; content.Children.Add(actions);
        var insert = new Button { Content = "插入到光标处" }; actions.Children.Add(insert);
        insert.Click += (_, _) => Editor?.InsertSnippet(example.Text);
        var copy = new Button { Content = "复制示例" }; actions.Children.Add(copy);
        copy.Click += (_, _) => { try { Clipboard.SetText(example.Text); copy.Content = "已复制"; } catch (System.Runtime.InteropServices.ExternalException) { copy.Content = "复制失败，请重试"; } };
        void SelectExample() { if (choices.SelectedItem is SyntaxExample item) { description.Text = item.Description; example.Text = item.Code; copy.Content = "复制示例"; } }
        choices.SelectionChanged += (_, _) => SelectExample(); SelectExample();
        Content = new Expander { Header = "语法说明与示例", Content = content, IsExpanded = false };
    }
}
