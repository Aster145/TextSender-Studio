using System.Globalization;
using System.Text;

namespace TextSenderStudio.Core;

/// <summary>Records confirmed reports, not UI requests that might never reach the bridge.</summary>
public sealed class ControllerRecorder
{
    private readonly object _gate = new();
    private readonly StringBuilder _script = new();
    private PadState _last = PadState.Neutral;
    private long _started, _lastAt;
    private bool _recording;
    public bool IsRecording { get { lock (_gate) return _recording; } }

    public void Start(PadState? initial = null)
    {
        lock (_gate)
        {
            _script.Clear(); _started = Environment.TickCount64; _lastAt = 0; _last = PadState.Neutral; _recording = true;
            // Explicit baseline makes playback independent of earlier held input.
            AppendState(PadState.Neutral);
            if (initial.HasValue) RecordCore(initial.Value, 0);
        }
    }

    public void Record(PadState state) { lock (_gate) RecordCore(state, Environment.TickCount64 - _started); }
    public void RecordAt(PadState state, long elapsedMs) { lock (_gate) RecordCore(state, elapsedMs); }
    private void RecordCore(PadState state, long elapsedMs)
    {
        if (!_recording || state == _last) return;
        elapsedMs = Math.Max(_lastAt, elapsedMs);
        AppendWait(elapsedMs - _lastAt);
        AppendState(state); _last = state; _lastAt = elapsedMs;
    }
    public string Stop() { lock (_gate) return StopCore(Environment.TickCount64 - _started); }
    public string StopAt(long elapsedMs) { lock (_gate) return StopCore(elapsedMs); }
    private string StopCore(long elapsedMs)
    {
        if (_recording)
        {
            RecordCore(PadState.Neutral, Math.Max(_lastAt, elapsedMs));
            _recording = false;
        }
        return _script.ToString();
    }
    private void AppendWait(long milliseconds)
    {
        // WAIT uses a signed 32-bit millisecond duration.
        while (milliseconds > 0)
        {
            long part = Math.Min(milliseconds, int.MaxValue);
            _script.Append("WAIT ").Append(part.ToString(CultureInfo.InvariantCulture)).AppendLine(); milliseconds -= part;
        }
    }
    private void AppendState(PadState state) => _script.Append("PAD_STATE \"").Append(state.ButtonNames).Append("\", ")
        .Append(state.LX).Append(", ").Append(state.LY).Append(", ").Append(state.RX).Append(", ").Append(state.RY).AppendLine();
}
