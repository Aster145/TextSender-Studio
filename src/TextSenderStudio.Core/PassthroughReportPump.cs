using System.Threading.Channels;

namespace TextSenderStudio.Core;

/// <summary>One ordered writer; pause invalidates queued key-downs before requesting release.</summary>
public sealed class PassthroughReportPump : IAsyncDisposable
{
    private sealed record Item(KeyboardReport? Report, int Generation, TaskCompletionSource? Completion);
    private readonly IInputBridge _bridge;
    private readonly Channel<Item> _queue = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _gate = new();
    private readonly Task _worker;
    private CancellationTokenSource _generationToken = new();
    private bool _enabled, _disposed;
    private int _generation, _queuedReports;
    public event Action<string>? Failed;

    public PassthroughReportPump(IInputBridge bridge)
    {
        _bridge = bridge;
        _worker = Task.Run(RunAsync);
    }

    public void Resume()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _enabled = true;
        }
    }

    // Called by a keyboard hook. No device I/O, waits or unbounded report accumulation.
    public bool Submit(KeyboardReport report)
    {
        lock (_gate)
        {
            if (!_enabled || _disposed || _queuedReports >= 64) return false;
            _queuedReports++;
            return _queue.Writer.TryWrite(new Item(report, _generation, null));
        }
    }

    public Task PauseAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            _enabled = false;
            _generation++;
            _generationToken.Cancel();
            _generationToken.Dispose();
            _generationToken = new();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Writer.TryWrite(new Item(null, _generation, completion));
            return completion.Task;
        }
    }

    private async Task RunAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync())
        {
            if (item.Report is null)
            {
                try { using var timeout = new CancellationTokenSource(3000); await _bridge.ReleaseAllAsync(timeout.Token).ConfigureAwait(false); item.Completion?.TrySetResult(); }
                catch (Exception ex) { item.Completion?.TrySetException(ex); }
                continue;
            }
            CancellationToken token;
            lock (_gate)
            {
                _queuedReports--;
                if (!_enabled || item.Generation != _generation) continue;
                token = _generationToken.Token;
            }
            try { await _bridge.SendKeyboardReportAsync(item.Report.Modifiers, item.Report.Keys, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch
            {
                lock (_gate) { _enabled = false; _generation++; }
                try { using var timeout = new CancellationTokenSource(3000); await _bridge.ReleaseAllAsync(timeout.Token).ConfigureAwait(false); } catch { }
                Notify("按键发送失败，直通已暂停；请检查设备连接。");
            }
        }
    }

    private void Notify(string message) { try { Failed?.Invoke(message); } catch { } }

    public async ValueTask DisposeAsync()
    {
        try { await PauseAsync().ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                _disposed = true;
                _enabled = false;
                _queue.Writer.TryComplete();
            }
            await _worker.ConfigureAwait(false);
            _generationToken.Dispose();
        }
    }
}
