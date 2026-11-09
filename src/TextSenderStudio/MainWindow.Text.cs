using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TextSenderStudio.Core;

namespace TextSenderStudio;

public partial class MainWindow
{
    private void TextSendChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing) UpdateDocumentSaveState();
    }

    private void SaveTextClick(object sender, RoutedEventArgs e)
    {
        try { SaveProject(); }
        catch (Exception error) { Dialogs.Error(this, error); }
    }

    private void LoadTextClick(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        var picker = new OpenFileDialog { Filter = "文本文件|*.txt;*.bas|所有文件|*.*", InitialDirectory = _store.ProjectPath(_directory) };
        if (picker.ShowDialog(this) != true) return;
        try { TextSendEditor.Text = File.ReadAllText(picker.FileName, Encoding.UTF8); SaveProject(); }
        catch (Exception error) { Dialogs.Error(this, error); }
    }

    private async void SendTextClick(object sender, RoutedEventArgs e)
    {
        if (_project == null || _operation != null) return;
        try
        {
            SaveProject();
            string text = TextSendEditor.Text;
            var actions = Actions(ProjectStore.Clone(_project));
            await RunOperation(ct => actions.TypeAsync(text, ct), true);
        }
        catch (Exception error) { Dialogs.Error(this, error); }
    }
}
