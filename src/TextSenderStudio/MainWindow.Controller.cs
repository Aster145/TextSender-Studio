using System.Windows;
using TextSenderStudio.Core;

namespace TextSenderStudio;

public partial class MainWindow
{
    private async void ControllerClick(object sender, RoutedEventArgs e) => await OpenControllerAsync(false);
    private async void RecordScriptClick(object sender, RoutedEventArgs e) => await OpenControllerAsync(true);

    private async Task OpenControllerAsync(bool record)
    {
        if (_operation != null || _project == null) return;
        try
        {
            SaveProject();
            await RunOperation(async token =>
            {
                var dialog = new ControllerWindow(this, _bridge, ProjectStore.Clone(_settings.DirectControl),
                    config =>
                    {
                        var next = ProjectStore.Clone(_settings);
                        next.DirectControl = ProjectStore.Clone(config);
                        _store.SaveSettings(next);
                        _settings = next;
                    },
                    source =>
                    {
                        if (string.IsNullOrWhiteSpace(source)) return;
                        InsertCode(source);
                        Log("录制脚本已插入编辑器，按 Ctrl+S 保存。");
                    }, record);
                using var registration = token.Register(() => Dispatcher.BeginInvoke(() => dialog.Close()));
                dialog.ShowDialog();
                await Task.CompletedTask;
            });
        }
        catch (Exception error) { Dialogs.Error(this, error); }
    }
}
