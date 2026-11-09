using System.Windows;
using System.Windows.Threading;
using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

namespace TextSenderStudio;

public partial class MainWindow
{
    private readonly DispatcherTimer _executionPositionTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(100) };
    private ScriptExecutionTracker? _executionTracker;
    private string? _executingSource;
    private bool _executionSourceMatches, _changingExecutionToggle;

    private void InitializeExecutionPosition()
    {
        _executionPositionTimer.Tick += (_, _) => RefreshExecutionPosition();
        ScriptEditor.TextChanged += (_, _) => CheckExecutingSource();
        MainTabs.SelectionChanged += (_, _) => RefreshExecutionPosition();
        Closed += (_, _) => _executionPositionTimer.Stop();
    }

    private void ExecutionPositionChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _changingExecutionToggle) return;
        var enabled = ShowExecutionPositionCheck.IsChecked == true;
        try
        {
            var next = ProjectStore.Clone(_settings);
            next.ShowExecutionPosition = enabled;
            _store.SaveSettings(next);
            _settings.ShowExecutionPosition = enabled;
        }
        catch (Exception error)
        {
            _changingExecutionToggle = true;
            try { ShowExecutionPositionCheck.IsChecked = _settings.ShowExecutionPosition; }
            finally { _changingExecutionToggle = false; }
            Dialogs.Error(this, error);
        }
        ConfigureExecutionPosition();
    }

    private void ConfigureExecutionPosition()
    {
        if (_executionTracker != null) _executionTracker.Enabled = _settings.ShowExecutionPosition;
        if (_settings.ShowExecutionPosition && _executingSource != null && !_closing)
            _executionPositionTimer.Start();
        else _executionPositionTimer.Stop();
        RefreshExecutionPosition();
    }

    private void CheckExecutingSource()
    {
        if (_executingSource == null) return;
        // Compare only on document/mode changes, not on each UI timer tick.
        _executionSourceMatches = ScriptEditor.Text == _executingSource;
        RefreshExecutionPosition();
    }

    private void RefreshExecutionPosition()
    {
        if (!_settings.ShowExecutionPosition || _executingSource == null || _closing)
        {
            ScriptEditor.ClearExecutionLine();
            ExecutionPositionStatus.Visibility = Visibility.Collapsed;
            return;
        }
        ExecutionPositionStatus.Visibility = Visibility.Visible;
        if (!_executionSourceMatches)
        {
            ScriptEditor.ClearExecutionLine();
            ExecutionPositionStatus.Text = "内容已修改，执行位置已隐藏（运行的是启动时的脚本）";
            return;
        }
        var line = _executionTracker?.CurrentLine ?? 0;
        ExecutionPositionStatus.Text = line > 0 ? $"正在执行 · 第 {line} 行" : "正在准备脚本…";
        if (line > 0 && ScriptEditor.IsVisible) ScriptEditor.ShowExecutionLine(line);
        else ScriptEditor.ClearExecutionLine();
    }

    private async Task RunScriptWithPositionAsync(string source, IScriptActions actions, CancellationToken token)
    {
        _executingSource = source;
        _executionSourceMatches = ScriptEditor.Text == source;
        var tracker = new ScriptExecutionTracker { Enabled = _settings.ShowExecutionPosition };
        _executionTracker = tracker;
        ConfigureExecutionPosition();
        AppendScriptOutput("开始", ScriptFileName.Text);
        var ending = "已完成";
        try { await _engine.RunAsync(source, actions, token, tracker, output => AppendScriptOutput(output.Kind == ScriptOutputKind.Print ? "PRINT" : "ALERT", output.Text)); }
        catch (OperationCanceledException) { ending = "已停止"; throw; }
        catch { ending = "执行失败"; throw; }
        finally
        {
            AppendScriptOutput("结束", ending);
            _executionPositionTimer.Stop();
            _executingSource = null;
            _executionTracker = null;
            ScriptEditor.ClearExecutionLine();
            ExecutionPositionStatus.Visibility = Visibility.Collapsed;
        }
    }
}
