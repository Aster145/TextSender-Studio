using System.Windows.Controls;
using System.Windows.Threading;
using TextSenderStudio;

internal static class ExecutionMarkerChecks
{
    public static void Run(MainWindow main, Action<bool, string> check)
    {
        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        var editToggle = (CheckBox)main.FindName("MainScriptEditToggle");
        bool? wasEditing = editToggle.IsChecked; editToggle.IsChecked = true;
        ((TabControl)main.FindName("MainTabs")).SelectedIndex = 0;
        string before = editor.Text;
        try
        {
            editor.Text = "PRINT ready\r\nWAIT 1000\r\nPRINT done";
            editor.Select(2, 4); main.UpdateLayout(); Pump();
            string text = editor.Text;
            int caret = editor.CaretIndex, selection = editor.SelectionStart, length = editor.SelectionLength;
            bool undo = editor.CanUndo;
            editor.ShowExecutionLine(2);
            check(editor.ExecutionLine == 2, "execution marker accepts one-based source line");
            check(editor.Text == text && editor.CaretIndex == caret && editor.SelectionStart == selection && editor.SelectionLength == length && editor.CanUndo == undo,
                "execution highlight preserves text, caret, selection and undo state");
            editor.ClearExecutionLine();
            check(editor.ExecutionLine == 0 && editor.SelectionStart == selection && editor.SelectionLength == length,
                "hiding execution highlight does not alter user selection");
            editor.ShowExecutionLine(999);
            check(editor.ExecutionLine == 0, "invalid source line is hidden instead of throwing");
            editor.ShowExecutionLine(2); editor.ScriptMode = false;
            check(editor.ExecutionLine == 0, "plain-text mode removes execution highlight");
            editor.ScriptMode = true;
            editor.ShowExecutionLine(2); editor.AppendText("\r\nPRINT changed");
            check(editor.ExecutionLine == 0, "editing source clears stale execution highlight");

            editor.Text = string.Join("\n", Enumerable.Range(1, 120).Select(i => "PRINT line" + i));
            editor.Select(0, 5); editor.ScrollToHome(); main.UpdateLayout(); Pump();
            caret = editor.CaretIndex; selection = editor.SelectionStart; length = editor.SelectionLength;
            editor.ShowExecutionLine(100); Pump();
            check(editor.GetFirstVisibleLineIndex() <= 99 && editor.GetLastVisibleLineIndex() >= 99,
                "execution highlight follows an offscreen source line");
            check(editor.CaretIndex == caret && editor.SelectionStart == selection && editor.SelectionLength == length,
                "following execution scrolls without moving the caret");
            editor.ScrollToHome(); Pump();
            int first = editor.GetFirstVisibleLineIndex();
            editor.ShowExecutionLine(100); Pump();
            check(editor.GetFirstVisibleLineIndex() == first, "same-line ticks do not fight manual scrolling");
            editor.ShowExecutionLine(101, false); Pump();
            check(editor.ExecutionLine == 101 && editor.GetFirstVisibleLineIndex() == first,
                "execution marker supports display without following");
            editor.ClearExecutionLine(); editor.ShowExecutionLine(3); Pump();
            editor.Select(0, 0); editor.SelectedText = "# note\n";
            editor.ShowExecutionLine(4, false); editor.ClearExecutionLine(); editor.Undo();
            check(editor.Text.StartsWith("PRINT line1\n"), "execution marker does not insert an undo operation");
        }
        finally
        {
            editor.ClearExecutionLine(); editor.ScriptMode = true; editor.Text = before;
            editor.CaretIndex = 0; editor.ScrollToHome(); editor.CloseCompletion();
            editToggle.IsChecked = wasEditing;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
