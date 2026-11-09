namespace TextSenderStudio.Core;

/// <summary>Serializes controller reports. The bridge itself maintains its 150 ms lease heartbeat.</summary>
public sealed class DirectControlPump : IAsyncDisposable
{
    private sealed record Request(PadState State, TaskCompletionSource? Released = null);
    private readonly IInputBridge _bridge;
    private readonly object _gate = new();
    private readonly Queue<Request> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _worker;
    private CancellationTokenSource? _inflight;
    private Task? _pause;
    private PadState? _lastQueued;
    private PadState _lastConfirmed = PadState.Neutral;
    private bool _disposing, _disposed, _faulted, _touched;
    private int _failureReported;
    public event Action<PadState>? Confirmed;
    public event Action<Exception>? Failed;
    public PadState LastConfirmed { get { lock (_gate) return _lastConfirmed; } }

    public DirectControlPump(IInputBridge bridge) { _bridge = bridge; _worker = Task.Run(ConsumeAsync); }

    public bool Submit(PadState state)
    {
        lock (_gate)
        {
            if (_disposing || _disposed || _faulted || _pause is { IsCompleted: false }) return false;
            if (_lastQueued == state) return true;
            if (_queue.Count >= 256)
            {
                _faulted = true; _queue.Clear(); _lastQueued = null; _inflight?.Cancel();
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pause = stopped.Task;
                _queue.Enqueue(new(PadState.Neutral, stopped));
                _signal.Release();
                ThreadPool.QueueUserWorkItem(_ => NotifyFailure(new IOException("手柄输入积压，已停止直通并释放按键。请重新打开直通窗口。")));
                return false;
            }
            _lastQueued = state; _queue.Enqueue(new(state)); _signal.Release(); return true;
        }
    }

    /// <summary>Discard pending input, cancel an in-flight report, then await a serialized release. Next Submit resumes.</summary>
    public Task PauseAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_pause is { IsCompleted: false }) return _pause;
            _queue.Clear(); _lastQueued = null; _inflight?.Cancel();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pause = completion.Task;
            _queue.Enqueue(new(PadState.Neutral, completion)); _signal.Release();
            return _pause;
        }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                Request? request;
                lock (_gate) request = _queue.TryDequeue(out var found) ? found : null;
                if (request is null) continue;
                if (request.Released is not null)
                {
                    try
                    {
                        if (_touched)
                        {
                            using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            await _bridge.ReleaseAllAsync(releaseTimeout.Token).ConfigureAwait(false);
                            _touched = false;
                        }
                        NotifyConfirmed(PadState.Neutral);
                        request.Released.TrySetResult();
                    }
                    catch (Exception error) { lock (_gate) _faulted = true; request.Released.TrySetException(error); NotifyFailure(error); }
                    continue;
                }
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                lock (_gate)
                {
                    // Pause can arrive after this item was dequeued but before sending.
                    if (_pause is { IsCompleted: false } || _faulted) continue;
                    _inflight = cancellation; _touched = true;
                }
                try
                {
                    var state = request.State;
                    await _bridge.SetPadStateAsync(state.ButtonNames, state.LX, state.LY, state.RX, state.RY, cancellation.Token).ConfigureAwait(false);
                    NotifyConfirmed(state);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception error)
                {
                    TaskCompletionSource[] barriers;
                    lock (_gate)
                    {
                        _faulted = true;
                        barriers = _queue.Where(r => r.Released is not null).Select(r => r.Released!).ToArray();
                        _queue.Clear(); _lastQueued = null;
                    }
                    try
                    {
                        using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await _bridge.ReleaseAllAsync(release.Token).ConfigureAwait(false);
                        _touched = false; NotifyConfirmed(PadState.Neutral);
                        foreach (var barrier in barriers) barrier.TrySetResult();
                    }
                    catch (Exception releaseError) { foreach (var barrier in barriers) barrier.TrySetException(releaseError); }
                    NotifyFailure(error);
                }
                finally { lock (_gate) if (ReferenceEquals(_inflight, cancellation)) _inflight = null; }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void NotifyFailure(Exception error)
    {
        // A release error must not recursively trigger UI pause/release retries.
        if (Interlocked.Exchange(ref _failureReported, 1) == 0) Failed?.Invoke(error);
    }

    private void NotifyConfirmed(PadState state)
    {
        lock (_gate) _lastConfirmed = state;
        Confirmed?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed || _disposing) return; _disposing = true; }
        try { await PauseAsync().ConfigureAwait(false); }
        finally
        {
            lock (_gate) _disposed = true;
            _lifetime.Cancel(); await _worker.ConfigureAwait(false);
            _lifetime.Dispose(); _signal.Dispose();
        }
    }
}
