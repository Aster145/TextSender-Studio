using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TextSenderStudio.Core;

namespace TextSenderStudio;

internal sealed class PassthroughWindow : Window
{
    private readonly KeyboardCaptureHook _hook = new();
    private readonly KeyboardReportState _keys = new();
    private readonly PassthroughReportPump _pump;
    private readonly TextBlock _status = new() { Text = "未启用", FontSize = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 20) };
    private readonly Button _start = new() { Content = "开始直通", Padding = new Thickness(16, 8, 16, 8) };
    private readonly Button _stop = new() { Content = "暂停并释放", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
    private bool _closing, _closeReady;

    public PassthroughWindow(Window owner, IInputBridge bridge)
    {
        Owner = owner;
        Title = "键盘直通 — TextSender Studio";
        Width = 560; Height = 330; MinWidth = 440; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(246, 250, 252));
        _pump = new PassthroughReportPump(bridge);
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "仅在此窗口中控制", FontSize = 22, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "开启后，按键发送到目标设备并屏蔽本机普通快捷键。切换到其他窗口会暂停并释放，回来后点击“开始直通”继续。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_start); buttons.Children.Add(_stop);
        var close = new Button { Content = "关闭", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(close); content.Children.Add(buttons);
        content.Children.Add(new TextBlock { Text = "支持 6 个普通键 + 8 个修饰键。不记录输入内容；Windows 安全组合键由系统保留。", FontSize = 12, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) });
        Content = content;
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += async (_, _) => await PauseAsync("已暂停，按键已释放。");
        close.Click += (_, _) => Close();
        Deactivated += async (_, _) => { if (!_closing && _hook.IsActive) await PauseAsync("窗口已失去焦点，直通已暂停。"); };
        _hook.Failed += _ => Dispatcher.BeginInvoke(() => PauseAsync("本机按键捕获已停止，请重新开始。"));
        _pump.Failed += message => Dispatcher.BeginInvoke(() => PauseAsync(message));
        Closing += ClosingAsync;
    }

    private async Task StartAsync()
    {
        if (_closing || !IsActive) return;
        _start.IsEnabled = false;
        try
        {
            await _pump.PauseAsync();
            if (_closing || !IsActive) return;
            _keys.Clear();
            _pump.Resume();
            _hook.Start(new WindowInteropHelper(this).Handle, OnKey);
            _status.Text = "直通中 · 点击“暂停并释放”结束";
            _stop.IsEnabled = true;
        }
        catch (Exception ex) { _hook.Stop(); _status.Text = ex.Message; await ReleaseQuietlyAsync(); }
        finally { _start.IsEnabled = !_hook.IsActive && !_closing; }
    }

    private void OnKey(int message, IntPtr wParam, IntPtr lParam)
    {
        var changed = _keys.HandleKeyMessage(message, wParam, lParam, out var report, out var warning);
        if (warning is not null) _status.Text = warning;
        if (changed && !_pump.Submit(report))
        {
            _hook.Stop();
            _keys.Clear();
            Dispatcher.BeginInvoke(() => PauseAsync("按键队列已满，已暂停直通。"));
        }
    }

    private async Task PauseAsync(string status)
    {
        _hook.Stop(); _keys.Clear(); _stop.IsEnabled = false; _start.IsEnabled = false;
        _status.Text = status;
        try { await _pump.PauseAsync(); }
        catch { _status.Text = "直通已停止，但设备未确认释放；请检查连接。"; }
        finally { if (!_closing) _start.IsEnabled = true; }
    }

    private async Task ReleaseQuietlyAsync() { try { await _pump.PauseAsync(); } catch { } }

    private async void ClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_closeReady) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _hook.Dispose(); _keys.Clear(); _start.IsEnabled = _stop.IsEnabled = false;
        try { await _pump.DisposeAsync(); } catch { }
        _closeReady = true;
        Close();
    }
}
