using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using FlashCap;
using TextSenderStudio.Core;

namespace TextSenderStudio.Vision;

public sealed record CameraInfo(int Index, string Name);

/// <summary>Explicitly started capture. Events run on the capture worker, never the UI thread.</summary>
public sealed class CaptureService : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _frameLock = new();
    private CancellationTokenSource? _session;
    private Task? _worker;
    private FrameSnapshot? _latest;
    private bool _disposed;
    public event Action<FrameSnapshot>? FrameReady;
    public event Action<string>? Error;
    public bool IsRunning => _worker is { IsCompleted: false };

    public static Task<IReadOnlyList<CameraInfo>> EnumerateCamerasAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<CameraInfo>>(() =>
    {
        token.ThrowIfCancellationRequested();
        return Descriptors().Select((d, i) => new CameraInfo(i, d.ToString())).ToArray();
    }, token);

    private static CaptureDeviceDescriptor[] Descriptors() => new CaptureDevices().EnumerateDescriptors()
        .Where(d => d.Characteristics.Length > 0).ToArray();

    public FrameSnapshot? GetLatestFrame()
    {
        lock (_frameLock) return _latest?.Copy();
    }

    public async Task StartAsync(CaptureSettings settings, CancellationToken token = default)
    {
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await StopCoreAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var source = settings.Source;
            var cameraIndex = settings.CameraIndex;
            var cameraWidth = settings.CameraWidth;
            var cameraHeight = settings.CameraHeight;
            var desktop = settings.DesktopRegion is { } r ? new PixelRegion { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height } : null;
            _session = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = _session.Token;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _worker = Task.Run(async () =>
            {
                try
                {
                    if (source.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        // Capture the first frame before marking the source ready.
                        Publish(CaptureDesktop(desktop));
                        ready.TrySetResult();
                        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false)) Publish(CaptureDesktop(desktop));
                    }
                    else if (source.Equals("Camera", StringComparison.OrdinalIgnoreCase) || source.Equals("Capture", StringComparison.OrdinalIgnoreCase))
                    {
                        var descriptors = Descriptors();
                        if (cameraIndex < 0 || cameraIndex >= descriptors.Length)
                            throw new InvalidOperationException("采集设备不存在，请刷新设备列表后重新选择。");
                        var descriptor = descriptors[cameraIndex];
                        var format = descriptor.Characteristics.OrderBy(c => Math.Abs((long)c.Width - cameraWidth) + Math.Abs((long)c.Height - cameraHeight)).First();
                        long lastFrame = 0;
                        using var device = await descriptor.OpenAsync(format, buffer =>
                        {
                            if (ct.IsCancellationRequested) return;
                            var now = Stopwatch.GetTimestamp();
                            if (now - Interlocked.Read(ref lastFrame) < Stopwatch.Frequency / 10) return;
                            Interlocked.Exchange(ref lastFrame, now);
                            try
                            {
                                using var stream = new MemoryStream(buffer.Buffer.ExtractImage());
                                using var bitmap = Image.FromStream(stream);
                                using var png = new MemoryStream();
                                bitmap.Save(png, ImageFormat.Png);
                                Publish(new FrameSnapshot(png.ToArray(), bitmap.Width, bitmap.Height, DateTimeOffset.UtcNow));
                                ready.TrySetResult();
                            }
                            catch (Exception ex) { ready.TrySetException(ex); ReportError(ex.Message); }
                        }).ConfigureAwait(false);
                        try
                        {
                            await device.StartAsync().ConfigureAwait(false);
                            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                        }
                        finally { await device.StopAsync().ConfigureAwait(false); }
                    }
                    else throw new ArgumentException("画面来源须为 Desktop 或 Camera。", nameof(settings));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { ready.TrySetCanceled(ct); }
                catch (Exception ex) { ready.TrySetException(ex); ReportError(ex.Message); }
                finally { lock (_frameLock) _latest = null; }
            }, CancellationToken.None);
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(12), token).ConfigureAwait(false); }
            catch { await StopCoreAsync().ConfigureAwait(false); throw; }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        if (_session is not null) await _session.CancelAsync().ConfigureAwait(false);
        if (_worker is not null) await _worker.ConfigureAwait(false);
        _session?.Dispose();
        _session = null;
        _worker = null;
        lock (_frameLock) _latest = null;
    }

    public static FrameSnapshot CaptureDesktop(PixelRegion? region = null)
    {
        var desktop = System.Windows.Forms.SystemInformation.VirtualScreen;
        var requested = region is null ? desktop : new Rectangle(region.X, region.Y, region.Width, region.Height);
        ValidateDesktopRegion(requested, desktop);
        using var bitmap = new Bitmap(requested.Width, requested.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(requested.Left, requested.Top, 0, 0, requested.Size, CopyPixelOperation.SourceCopy);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return new FrameSnapshot(png.ToArray(), bitmap.Width, bitmap.Height, DateTimeOffset.UtcNow);
    }

    public static void ValidateDesktopRegion(Rectangle requested, Rectangle desktop)
    {
        if (requested.Width <= 0 || requested.Height <= 0 || requested.Left < desktop.Left || requested.Top < desktop.Top ||
            (long)requested.Left + requested.Width > (long)desktop.Left + desktop.Width ||
            (long)requested.Top + requested.Height > (long)desktop.Top + desktop.Height)
            throw new ArgumentOutOfRangeException(nameof(requested), "屏幕选区须位于虚拟桌面边界内，且宽高大于 0。");
    }

    private void Publish(FrameSnapshot frame)
    {
        lock (_frameLock) _latest = frame;
        FrameReady?.Invoke(frame.Copy());
    }
    private void ReportError(string message) { try { Error?.Invoke(message); } catch { /* UI subscriber must not prevent capture cleanup. */ } }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { if (_disposed) return; _disposed = true; await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }
}
