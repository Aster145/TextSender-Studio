using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TextSenderStudio;
using TextSenderStudio.Core;

internal static class SaveShortcutChecks
{
    public static void Run(MainWindow main, ProjectStore store, Action<bool, string> check)
    {
        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        var editToggle = (CheckBox)main.FindName("MainScriptEditToggle");
        bool? wasEditing = editToggle.IsChecked; editToggle.IsChecked = true;
        var tabs = (TabControl)main.FindName("MainTabs");
        var textEditor = (TextBox)main.FindName("TextSendEditor");
        var directory = (string)typeof(MainWindow).GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var project = store.Load(directory);
        var save = typeof(MainWindow).GetMethod("SaveScriptClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        tabs.SelectedIndex = 0;
        string original = editor.Text;
        string originalText = textEditor.Text;
        try
        {
            const string input = "FOR 2\nWAIT 0\n    NEXT\n";
            const string formatted = "FOR 2\n    WAIT 0\nNEXT\n";
            editor.Text = input;
            save.Invoke(main, [main, new RoutedEventArgs()]);
            check(editor.Text == formatted && File.ReadAllText(store.Resolve(directory, project.MainScript)) == formatted,
                "Save button formats the editor and writes corrected indentation");
            var binding = main.InputBindings.OfType<KeyBinding>().Single(k => k.Key == Key.S && k.Modifiers == ModifierKeys.Control);
            check(binding.Command == ApplicationCommands.Save && ApplicationCommands.Save.CanExecute(null, editor),
                "Ctrl+S is bound to the save command from within the code editor");
            editor.Text = input;
            ApplicationCommands.Save.Execute(null, editor);
            check(editor.Text == formatted && File.ReadAllText(store.Resolve(directory, project.MainScript)) == formatted,
                "routed keyboard save uses the same formatting and disk write as Save");
            editor.RefreshStructure(); editor.Foldings.Single().IsFolded = true;
            ApplicationCommands.Save.Execute(null, editor);
            check(editor.Foldings.Single().IsFolded && File.ReadAllText(store.Resolve(directory, project.MainScript)) == formatted,
                "saving a folded block writes its complete body without expanding or dropping source");
            editor.Text = input; editToggle.IsChecked = false;
            ApplicationCommands.Save.Execute(null, editor);
            check(editor.Text == input && File.ReadAllText(store.Resolve(directory, project.MainScript)) == input
                && ((TextBlock)main.FindName("SaveState")).Text == "已保存",
                "locked script can save existing edits without formatting or changing the document");
            editToggle.IsChecked = true; ApplicationCommands.Save.Execute(null, editor);
            tabs.SelectedIndex = 1;
            const string raw = "    FOR\nNEXT\n";
            textEditor.Text = raw;
            ApplicationCommands.Save.Execute(null, textEditor);
            check(textEditor.Text == raw && File.ReadAllText(store.Resolve(directory, "draft.txt")) == raw && editor.Text == formatted,
                "Ctrl+S preserves whitespace when saving ordinary text");
            var projects = (ComboBox)main.FindName("ProjectPicker");
            int current = projects.SelectedIndex;
            projects.SelectedIndex = current == 0 ? 1 : 0;
            check(textEditor.Text != raw, "text draft switches independently with project");
            projects.SelectedIndex = current;
            check(textEditor.Text == raw && editor.Text == formatted, "returning project restores both separate documents");
        }
        finally
        {
            editToggle.IsChecked = true;
            tabs.SelectedIndex = 0; editor.Text = original; textEditor.Text = originalText;
            save.Invoke(main, [main, new RoutedEventArgs()]);
            editToggle.IsChecked = wasEditing;
        }
    }
}
