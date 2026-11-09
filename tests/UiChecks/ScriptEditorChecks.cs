using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TextSenderStudio;
using TextSenderStudio.Core.Scripting;

internal static class ScriptEditorChecks
{
    public static void Run(MainWindow main, Action<bool, string> check)
    {
        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        var editToggle = (CheckBox)main.FindName("MainScriptEditToggle");
        bool? wasEditing = editToggle.IsChecked;
        editToggle.IsChecked = true;
        ((TabControl)main.FindName("MainTabs")).SelectedIndex = 0;
        main.UpdateLayout();
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
            check(editor.Text == "CALL phrase_hello" && !editor.CompletionOpen && editor.CaretIndex == editor.Text.Length, "completion inserts selected function and leaves caret after it");
            editor.Undo(); check(editor.Text == "CALL phrase_he", "completion remains one native undo operation");
            check(EditorSyntax.Complete("$score = @画", 11, editor.ResourceCompletions).Single().Text == "@画面标签", "image label completion supports Chinese names");
            check(EditorSyntax.Complete("# CALL ph", 9, editor.ResourceCompletions).Count == 0 && EditorSyntax.Complete("SEND_TEXT \"ph", 13, editor.ResourceCompletions).Count == 0, "completion does not alter comments or strings");
            editor.Text = "IF 1 == 1"; editor.CaretIndex = editor.Text.Length; editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n") == "IF 1 == 1\n    ", "Enter indents new conditional block");
            editor.CloseCompletion(); editor.Text = "WAIT 500"; editor.Select(editor.Text.Length, 0);
            var enter = PressEnter(editor);
            check(enter.Handled && editor.Text.Replace("\r\n", "\n") == "WAIT 500\n" && editor.CaretIndex == editor.Text.Length && editor.TextArea.Caret.Line == 2 && editor.SelectionLength == 0,
                $"routed Enter at line end moves caret onto next line (line={editor.TextArea.Caret.Line}, caret={editor.CaretIndex})");
            editor.Undo(); check(editor.Text == "WAIT 500", "routed Enter is one undo operation");
            editor.Text = "FOR 2\r\n    WAIT 500\r\nNEXT"; editor.Select(editor.Text.IndexOf("\r\nNEXT", StringComparison.Ordinal), 0);
            PressEnter(editor);
            check(editor.Text == "FOR 2\r\n    WAIT 500\r\n    \r\nNEXT" && editor.TextArea.Caret.Line == 3 && editor.TextArea.Caret.Column == 5,
                "routed Enter in indented CRLF source places caret after next-line indentation");
            editor.Text = "PRINT \"文字\""; editor.Select(editor.Text.Length, 0); PressEnter(editor);
            check(editor.Text.Replace("\r\n", "\n") == "PRINT \"文字\"\n" && editor.TextArea.Caret.Line == 2,
                "routed Enter after quoted payload preserves content and moves to next line");
            editor.Text = "CALL phrase_hello"; editor.Select(editor.Text.Length, 0); editor.ShowCompletion(); PressEnter(editor);
            check(editor.Text.Replace("\r\n", "\n") == "CALL phrase_hello\n" && editor.TextArea.Caret.Line == 2 && !editor.CompletionOpen,
                "Enter with an already complete suggestion inserts a newline");
            editor.Text = "CALL phrase_he"; editor.Select(editor.Text.Length, 0); editor.ShowCompletion(); PressEnter(editor);
            check(editor.Text == "CALL phrase_hello" && editor.CaretIndex == editor.Text.Length && !editor.CompletionOpen,
                "Enter still accepts a partial suggestion and keeps caret at completion end");
            editor.Text = "WAIT 500"; editor.Select(5, 3); editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n") == "WAIT \n", "Enter replaces selected text instead of retaining it");
            editor.Undo(); check(editor.Text == "WAIT 500", "Enter selection replacement is one undo operation");
            editor.Text = "PRINT 'one'\nPRINT 'two'"; editor.Select(6, editor.Text.Length - 6); editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n") == "PRINT \n", "Enter removes a selection spanning multiple source lines");
            editor.Text = "IF 1 == 1\n    PRINT ready\n    ENDIF"; editor.CaretIndex = editor.Text.Length; editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n").EndsWith("\nENDIF\n"), "closing block aligns to matching opening block");
            editor.Text = "IF 1 == 1\n    IF 1 == 1\n        PRINT nested\n    ENDIF\n    ELSE"; editor.CaretIndex = editor.Text.Length; editor.InsertIndentedNewLine();
            check(editor.Text.Replace("\r\n", "\n").EndsWith("\nELSE\n    "), "ELSE indentation skips already-closed nested blocks");
            editor.Text = "A 100\nWAIT 10\nB 100"; editor.Select(0, "A 100\nWAIT 10".Length); editor.IndentSelection(false);
            check(editor.Text == "    A 100\n    WAIT 10\nB 100", "Tab indents selected lines only");
            editor.IndentSelection(true); check(editor.Text == "A 100\nWAIT 10\nB 100", "Shift+Tab restores selected line indentation");
            editor.Text = "FUNC greet()\n    PRINT hello\nENDFUNC\nCALL greet";
            check(editor.NavigateAt(editor.Text.LastIndexOf("greet", StringComparison.Ordinal)) && editor.SelectionStart == 5 && editor.SelectedText == "greet", "function navigation selects local FUNC definition");
            editor.Text = "FUNC 提醒_一次()\n    PRINT 中文函数\nENDFUNC\nCALL 提醒_一次";
            check(editor.NavigateAt(editor.Text.LastIndexOf("提醒_一次", StringComparison.Ordinal)) && editor.SelectionStart == 5 && editor.SelectedText == "提醒_一次", "Chinese function navigation selects exact definition");
            editor.Text = "FUNC 提醒_一次()\n    PRINT 就绪\nENDFUNC\nCALL 提醒"; editor.CaretIndex = editor.Text.Length;
            editor.ShowCompletion();
            check(editor.VisibleCompletions.Any(c => c.Text == "提醒_一次"), "Chinese local function appears in CALL completion");
            editor.AcceptCompletion();
            check(editor.Text.EndsWith("CALL 提醒_一次"), "Chinese function completion inserts full name");

            editor.Text = "# IF ignored\nFUNC 提醒()\n    IF $次数 > 2\n        SEND_TEXT \"FOR 3 # 字符串\"\n    ENDIF\nENDFUNC\nCALL 提醒";
            editor.RefreshStructure(); main.UpdateLayout();
            check(editor.Foldings.Count == 2, "folding detects nested actual blocks, ignores comment and string keywords");
            string fullSource = editor.Text;
            editor.Foldings[0].IsFolded = true;
            check(editor.Text == fullSource && editor.Foldings[0].IsFolded, "folding changes only display and retains complete source");
            editor.ShowExecutionLine(4, false);
            check(!editor.Foldings[0].IsFolded && editor.ExecutionLine == 4, "execution marker expands the containing folded block");
            editor.ClearExecutionLine();
            var kinds = editor.HighlightSpans.Select(s => s.Kind).ToHashSet();
            check(new[] { ScriptHighlightKind.Keyword, ScriptHighlightKind.Function, ScriptHighlightKind.String, ScriptHighlightKind.Comment, ScriptHighlightKind.Variable, ScriptHighlightKind.Number }.All(kinds.Contains), "syntax colors distinguish keywords, functions, strings, comments, variables and numbers");
            int quotedFor = editor.Text.IndexOf("FOR 3", StringComparison.Ordinal);
            check(editor.HighlightSpans.Single(s => s.Start <= quotedFor && s.End > quotedFor).Kind == ScriptHighlightKind.String, "quoted block keywords retain string highlighting");
            editor.ScriptMode = false;
            check(editor.Foldings.Count == 0 && editor.HighlightSpans.Count == 0, "plain-text mode removes folding and syntax highlighting");
            editor.ScriptMode = true;
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
            editToggle.IsChecked = wasEditing;
        }
    }

    private static KeyEventArgs PressEnter(ScriptEditorBox editor)
    {
        var source = PresentationSource.FromVisual(editor) ?? throw new InvalidOperationException("Editor must be in a rendered test window.");
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        editor.TextArea.RaiseEvent(preview);
        if (!preview.Handled)
            editor.TextArea.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
        return preview;
    }
}
