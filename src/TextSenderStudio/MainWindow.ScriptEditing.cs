using System.Windows;
using System.Windows.Media;

namespace TextSenderStudio;

public partial class MainWindow
{
    private string _savedScriptText = "", _savedSendText = "";
    private bool _hasSavedDocuments;
    private bool CanEditMainScript => _project != null && _operation == null && !_closing && MainScriptEditToggle.IsChecked == true;

    private void InitializeScriptEditing()
    {
        ScriptEditor.TextChanged += (_, _) => { if (!_initializing) UpdateDocumentSaveState(); };
        TextSendEditor.TextChanged += (_, _) => { if (!_initializing) UpdateDocumentSaveState(); };
        UpdateScriptEditingState();
    }

    private void MarkDocumentsLoaded(string script, string text)
    {
        _savedScriptText = script; _savedSendText = text; _hasSavedDocuments = true;
        MainScriptEditToggle.IsChecked = false;
        UpdateDocumentSaveState(); UpdateScriptEditingState();
    }

    private void MarkDocumentsSaved(string script, string text)
    {
        _savedScriptText = script; _savedSendText = text; _hasSavedDocuments = true;
        UpdateDocumentSaveState();
    }

    private void UpdateDocumentSaveState()
    {
        if (!_hasSavedDocuments || ScriptEditor == null || TextSendEditor == null) return;
        SetSavedStatus(SaveState, ScriptEditor.Text == _savedScriptText);
        SetSavedStatus(TextSaveState, TextSendEditor.Text == _savedSendText);
    }

    private static void SetSavedStatus(System.Windows.Controls.TextBlock label, bool saved)
    {
        label.Text = saved ? "已保存" : "未保存";
        label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, saved ? "SuccessBrush" : "DangerBrush");
    }

    private void ScriptEditChanged(object sender, RoutedEventArgs e) => UpdateScriptEditingState();

    private void UpdateScriptEditingState()
    {
        if (ScriptEditor == null || MainScriptEditToggle == null) return;
        bool enabled = CanEditMainScript;
        ScriptEditor.IsReadOnly = !enabled;
        if (!enabled) ScriptEditor.CloseCompletion();
        MainScriptEditToggle.IsEnabled = _project != null && _operation == null && !_closing;
        LoadScriptButton.IsEnabled = InsertFunctionButton.IsEnabled = RecordScriptButton.IsEnabled = enabled;
        CommentScriptButton.IsEnabled = UncommentScriptButton.IsEnabled = enabled;
        // Saving existing edits remains available after locking. The save handler
        // only formats when editing is enabled, so a locked document is unchanged.
        SaveScriptButton.IsEnabled = _project != null && !_closing;
    }

    private void CommentScriptClick(object sender, RoutedEventArgs e) { if (CanEditMainScript) ScriptEditor.CommentSelection(); }
    private void UncommentScriptClick(object sender, RoutedEventArgs e) { if (CanEditMainScript) ScriptEditor.UncommentSelection(); }
}
