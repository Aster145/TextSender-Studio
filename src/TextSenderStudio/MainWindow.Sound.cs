using System.Windows;

namespace TextSenderStudio;

public partial class MainWindow
{
    private void ManageSoundsClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _operation != null) return;
        try
        {
            SaveProject();
            var dialog = new SoundEditorWindow(this, _project);
            if (dialog.ShowDialog() != true) return;
            _store.Save(_directory, dialog.Result);
            _project = dialog.Result;
            RefreshResources();
        }
        catch (Exception error) { Dialogs.Error(this, error); }
    }
}
