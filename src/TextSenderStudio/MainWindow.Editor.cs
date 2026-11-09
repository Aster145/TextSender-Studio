using System.Windows;

namespace TextSenderStudio;

public partial class MainWindow
{
    private void InitializeScriptEditor()
    {
        ScriptEditor.NavigateResource = NavigateScriptResource;
        ScriptEditor.CaretStatusChanged += (_, _) =>
        {
            if (FindName("EditorCaretStatus") is System.Windows.Controls.TextBlock label) label.Text = ScriptEditor.CaretStatus;
        };
        RefreshEditorContext();
    }

    private void RefreshEditorContext()
    {
        if (ScriptEditor == null) return;
        ScriptEditor.ScriptMode = _mode == 0;
        if (FindName("ScriptSyntaxHelp") is FrameworkElement help) help.Visibility = _mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_project == null) { ScriptEditor.ResourceCompletions = []; return; }
        ScriptEditor.ResourceCompletions = _project.Phrases.Select(p => new EditorCompletion(p.FunctionName, "短语 · " + p.Name))
            .Concat(_project.Shortcuts.Select(p => new EditorCompletion(p.FunctionName, "启动脚本 · " + p.Name)))
            .Concat(_project.HttpActions.Select(p => new EditorCompletion(p.FunctionName, "HTTP 动作 · " + p.Name)))
            .Concat(_project.Sounds.Select(p => new EditorCompletion(p.FunctionName, "声音 · " + p.Name)))
            .Concat(_project.Labels.Select(p => new EditorCompletion("@" + p.Name, "图像标签"))).ToArray();
    }

    private bool NavigateScriptResource(string name)
    {
        if (_project == null) return false;
        if (name.StartsWith('@'))
        {
            var label = _project.Labels.FirstOrDefault(p => p.Name == name[1..]); if (label == null) return false;
            MainTabs.SelectedIndex = 1; LabelsList.SelectedItem = label; LabelsList.ScrollIntoView(label); return true;
        }
        var http = _project.HttpActions.FirstOrDefault(p => p.FunctionName == name);
        if (http != null) { MainTabs.SelectedIndex = 2; HttpList.SelectedItem = http; HttpList.ScrollIntoView(http); return true; }
        bool phrase = _project.Phrases.Any(p => p.FunctionName == name);
        bool shortcut = _project.Shortcuts.Any(p => p.FunctionName == name);
        bool sound = _project.Sounds.Any(p => p.FunctionName == name);
        if (!phrase && !shortcut && !sound) return false;
        if (_operation != null) { Log("请先停止正在执行的操作，再编辑函数。"); return true; }
        try
        {
            SaveProject();
            if (sound)
            {
                var dialog = new SoundEditorWindow(this, _project, name);
                if (dialog.ShowDialog() == true) { _project = dialog.Result; _store.Save(_directory, _project); RefreshResources(); RefreshEditorContext(); }
            }
            else
            {
                var dialog = new ResourceEditorWindow(this, _project, phrase, name);
                if (dialog.ShowDialog() == true) { _project = dialog.Result; _store.Save(_directory, _project); RefreshResources(); RefreshEditorContext(); }
            }
        }
        catch (Exception error) { Dialogs.Error(this, error); }
        return true;
    }
}
