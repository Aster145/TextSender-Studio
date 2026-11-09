using System.Windows;

namespace TextSenderStudio;

public partial class MainWindow
{
    private bool _syncingManualControls;

    private void SyncManualControls()
    {
        _syncingManualControls = true;
        try
        {
            ManualControlsCheck.IsChecked = _settings.ShowManualControls;
            ManualPanel.Visibility = _settings.ShowManualControls ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncingManualControls = false; }
    }

    private void ManualControlsChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _syncingManualControls) return;
        _settings.ShowManualControls = ManualControlsCheck.IsChecked == true;
        ManualPanel.Visibility = _settings.ShowManualControls ? Visibility.Visible : Visibility.Collapsed;
        _store.SaveSettings(_settings);
    }
}
