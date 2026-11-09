using System.Windows.Controls;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core.Scripting;

internal static class ScriptEditorChecks
{
    public static void Run(MainWindow main, Action<bool, string> check)
    {
        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        var mode = (ComboBox)main.FindName("EditorMode");
        ((TabControl)main.FindName("MainTabs")).SelectedIndex = 0;
        main.UpdateLayout();
        mode.SelectedIndex = 0;
        string before = editor.Text;
        var resourcesBefore = editor.ResourceCompletions;
        var navigateBefore = editor.NavigateResource;
        try
        {
            var compiler = new StudioScriptEngine();
            foreach (var example in EditorSyntax.Examples)
            {
                var errors = compiler.Validate(example.Code, ["phrase_hello", "start_tools", "robot_shortpress", "reminder"]).Where(d => d.IsError).ToArray();
                check(errors.Length == 0, "syntax example compiles: " + example.Title + (errors.Length > 0 ? " " + string.Join("; ", errors.Select(e => e.ToString())) : ""));
            }

            editor.ResourceCompletions = [new("phrase_hello", "短语"), new("robot_shortpress", "HTTP"), new("@画面标签", "图像标签")];
            editor.Text = "CALL phrase_he"; editor.CaretIndex = editor.Text.Length;
            editor.ShowCompletion();
            check(editor.CompletionOpen && editor.VisibleCompletions.Select(c => c.Text).SequenceEqual(["phrase_hello"]), "CALL completion filters current project functions");
            editor.AcceptCompletion();
            check(editor.Text == "CALL phrase_hello" && !editor.CompletionOpen, "completion inserts selected function at caret");
            editor.Undo(); check(editor.Text == "CALL phrase_he", "completion remains one native undo operation");
            check(EditorSyntax.Complete("$score = @画", 11, editor.ResourceCompletions).Single().Text == "@画面标签", "image label completion supports Chinese names");
            check(EditorSyntax.Complete("# CALL ph", 9, editor.ResourceCompletions).Count == 0 && EditorSyntax.Complete("SEND_TEXT \"ph", 13, editor.ResourceCompletions).Count == 0, "completion does not alter comments or strings");
            editor.Text = "IF 1 == 1"; editor.CaretIndex = editor.Text.Length; editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n") == "IF 1 == 1\n    ", "Enter indents new conditional block");
            editor.Text = "IF 1 == 1\n    PRINT ready\n    ENDIF"; editor.CaretIndex = editor.Text.Length; editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n").EndsWith("\nENDIF\n"), "closing block aligns to matching opening block");
            editor.Text = "IF 1 == 1\n    IF 1 == 1\n        PRINT nested\n    ENDIF\n    ELSE"; editor.CaretIndex = editor.Text.Length; editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n").EndsWith("\nELSE\n    "), "ELSE indentation skips already-closed nested blocks");
            editor.Text = "A 100\nWAIT 10\nB 100"; editor.Select(0, "A 100\nWAIT 10".Length); editor.IndentSelection(false);
            check(editor.Text == "    A 100\n    WAIT 10\nB 100", "Tab indents selected lines only");
            editor.IndentSelection(true); check(editor.Text == "A 100\nWAIT 10\nB 100", "Shift+Tab restores selected line indentation");
            editor.Text = "FUNC greet()\n    PRINT hello\nENDFUNC\nCALL greet";
            check(editor.NavigateAt(editor.Text.LastIndexOf("greet", StringComparison.Ordinal)) && editor.SelectionStart == 5 && editor.SelectedText == "greet", "function navigation selects local FUNC definition");
            string? navigation = null; editor.NavigateResource = name => { navigation = name; return true; };
            editor.Text = "CALL robot_shortpress";
            check(editor.NavigateAt(editor.Text.IndexOf("robot_shortpress", StringComparison.Ordinal)) && navigation == "robot_shortpress", "resource function navigation dispatches exact function name");
            editor.ScriptMode = false; editor.Text = "WAIT"; editor.CaretIndex = 4; editor.ShowCompletion();
            check(!editor.CompletionOpen && !editor.NavigateAt(2), "plain-text mode disables completion and function navigation");
            editor.ScriptMode = true;
            editor.Text = string.Join("\n", Enumerable.Range(1, 120).Select(i => "PRINT line" + i));
            editor.UpdateLayout();
            var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false); Dispatcher.PushFrame(frame);
            int lineStart = editor.GetCharacterIndexFromLineIndex(100);
            editor.CaretIndex = lineStart; editor.ScrollToLine(100);
            check(editor.LineCount == 120 && editor.CaretStatus.StartsWith("行 101"), $"line count and caret position track long scripts (lines={editor.LineCount}, caret={editor.CaretIndex}, {editor.CaretStatus}, visible={editor.IsVisible}, size={editor.ActualWidth}x{editor.ActualHeight})");
        }
        finally
        {
            editor.CloseCompletion(); editor.ScriptMode = true; editor.ResourceCompletions = resourcesBefore; editor.NavigateResource = navigateBefore;
            editor.Text = before; editor.CaretIndex = 0; editor.ScrollToHome();
        }
    }
}
