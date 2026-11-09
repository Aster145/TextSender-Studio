using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TextSenderStudio;
using TextSenderStudio.Core;

internal static class ScriptEditingChecks
{
    public static void Run(MainWindow main, ProjectStore store, Action<bool, string> check)
    {
        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        var text = (TextBox)main.FindName("TextSendEditor");
        var toggle = (CheckBox)main.FindName("MainScriptEditToggle");
        var tabs = (TabControl)main.FindName("MainTabs");
        var originalTab = tabs.SelectedItem;
        var scriptStatus = (TextBlock)main.FindName("SaveState");
        var textStatus = (TextBlock)main.FindName("TextSaveState");
        string original = editor.Text, originalText = text.Text;
        bool? originalToggle = toggle.IsChecked;
        string savedScript = Field<string>(main, "_savedScriptText"), savedText = Field<string>(main, "_savedSendText");
        string directory = Field<string>(main, "_directory");
        string scriptPath = store.Resolve(directory, store.Load(directory).MainScript);
        string disk = File.ReadAllText(scriptPath);
        try
        {
            editor.Text = "WAIT 500"; text.Text = "文字草稿";
            Call(main, "MarkDocumentsLoaded", editor.Text, text.Text);
            check(toggle.IsChecked == false && editor.IsReadOnly && scriptStatus.Text == "已保存" && textStatus.Text == "已保存", "loaded documents start saved with script editing locked");
            check(Color(scriptStatus.Foreground) == Color((Brush)main.FindResource("SuccessBrush")), "saved indicator uses green success color");
            editor.InsertSnippet("PRINT forbidden"); editor.InsertIndentedNewLine(); editor.IndentSelection(false); editor.CommentSelection();
            check(editor.Text == "WAIT 500", "locked editor rejects snippet, indentation, newline and comment mutation APIs");
            check(new[] { "LoadScriptButton", "InsertFunctionButton", "RecordScriptButton", "CommentScriptButton", "UncommentScriptButton" }.All(n => !((Button)main.FindName(n)).IsEnabled), "script mutation controls are disabled while locked");

            toggle.IsChecked = true;
            check(!editor.IsReadOnly && ((Button)main.FindName("CommentScriptButton")).IsEnabled, "edit switch enables script editing and batch comments");
            editor.AppendText("\nPRINT changed");
            check(scriptStatus.Text == "未保存" && Color(scriptStatus.Foreground) == Color((Brush)main.FindResource("DangerBrush")) && textStatus.Text == "已保存", "only edited script becomes red unsaved");
            editor.Undo();
            check(scriptStatus.Text == "已保存" && Color(scriptStatus.Foreground) == Color((Brush)main.FindResource("SuccessBrush")), "undo back to saved script restores green saved indicator");
            tabs.SelectedItem = main.FindName("TextSendTab"); main.UpdateLayout(); text.Focus();
            text.IsUndoEnabled = false; text.IsUndoEnabled = true;
            text.Select(text.Text.Length, 0); text.SelectedText = " changed";
            check(textStatus.Text == "未保存" && scriptStatus.Text == "已保存", "ordinary text has independent dirty state");
            check(text.CanUndo, "visible text editor records a native undoable edit");
            text.Undo(); check(textStatus.Text == "已保存", "undo text draft back to snapshot restores saved state");
            tabs.SelectedIndex = 0;
            editor.AppendText("\nPRINT preserved"); toggle.IsChecked = false;
            check(editor.IsReadOnly && scriptStatus.Text == "未保存" && editor.Text.EndsWith("PRINT preserved") && File.ReadAllText(scriptPath) == disk, "locking preserves unsaved edits without saving or discarding them");
            Call(main, "MarkDocumentsSaved", editor.Text, text.Text);
            check(scriptStatus.Text == "已保存" && editor.IsReadOnly, "successful save baseline can update while editor stays locked");

            toggle.IsChecked = true;
            using var running = new CancellationTokenSource();
            typeof(MainWindow).GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, running);
            Call(main, "UpdateScriptEditingState");
            check(editor.IsReadOnly && !toggle.IsEnabled && !((Button)main.FindName("RecordScriptButton")).IsEnabled, "active operation temporarily locks script and mutation controls");
            typeof(MainWindow).GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, null);
            Call(main, "UpdateScriptEditingState");
            check(!editor.IsReadOnly && toggle.IsChecked == true, "operation completion restores explicitly enabled editing");

            editor.Text = "    WAIT 1\r\n    # PRINT ready\r\n\tA 100\r\nWAIT 2";
            editor.Select(0, editor.Text.LastIndexOf("WAIT 2", StringComparison.Ordinal));
            string source = editor.Text;
            check(editor.CommentSelection() && editor.Text == "    # WAIT 1\r\n    # PRINT ready\r\n\t# A 100\r\nWAIT 2", "batch comment respects indentation, existing comments and selection ending at next line start");
            check(!editor.CommentSelection(), "comment operation is idempotent for already-commented selected lines");
            editor.Undo(); check(editor.Text == source, "batch comment is one undo operation with original newlines intact");
            editor.Text = "    # WAIT 1\n\t#A 100\n\nWAIT 2";
            editor.Select(0, editor.Text.IndexOf("WAIT 2", StringComparison.Ordinal));
            check(editor.UncommentSelection() && editor.Text == "    WAIT 1\n\tA 100\n\nWAIT 2", "batch uncomment removes one comment marker and optional spacer while preserving blank lines");

            editor.Text = "FOR 2\n    WAIT 1\nNEXT\nPRINT ready"; editor.RefreshStructure();
            editor.Foldings.Single().IsFolded = true;
            editor.Select(0, editor.Text.IndexOf("PRINT", StringComparison.Ordinal));
            check(editor.ToggleCommentSelection() && editor.Text.StartsWith("# FOR 2\n    # WAIT 1\n# NEXT\nPRINT"), "batch toggle includes the full folded block body");
            check(editor.ToggleCommentSelection() && editor.Text == "FOR 2\n    WAIT 1\nNEXT\nPRINT ready", "toggle on fully commented block restores all source lines");
            editor.Text = "WAIT 1\nWAIT 2"; editor.CaretIndex = 3;
            check(editor.CommentSelection() && editor.Text == "# WAIT 1\nWAIT 2" && editor.CaretIndex == 5 && editor.SelectionLength == 0, "no-selection comment applies to current line and preserves caret token position");
            editor.ScriptMode = false;
            check(!editor.CommentSelection() && !editor.UncommentSelection(), "plain text mode does not apply script comment operations");
            editor.ScriptMode = true;
        }
        finally
        {
            typeof(MainWindow).GetField("_operation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, null);
            editor.ScriptMode = true; editor.Text = original; text.Text = originalText;
            Call(main, "MarkDocumentsSaved", savedScript, savedText);
            toggle.IsChecked = originalToggle; Call(main, "UpdateScriptEditingState");
            tabs.SelectedItem = originalTab;
        }
    }

    private static System.Windows.Media.Color Color(Brush brush) => ((SolidColorBrush)brush).Color;
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Call(MainWindow window, string name, params object[] arguments) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
}
