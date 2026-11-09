using EasyCon.Script.Bytecode;

namespace TextSenderStudio.Core.Scripting;

/// <summary>
/// A thread-safe, pull-only execution marker. Toggle Enabled at any time, including
/// during a host WAIT or request. The UI may poll at 100 ms; no instruction posts
/// UI work. Fast statements between samples are intentionally not all displayed.
/// </summary>
public sealed class ScriptExecutionTracker
{
    private sealed record Session(EcxImage Image, EcxExecutionPosition Position, int SourceLines, string SourceModule);
    private Session? _session;
    private int _enabled;
    public bool Enabled { get => Volatile.Read(ref _enabled) != 0; set => Volatile.Write(ref _enabled, value ? 1 : 0); }
    public bool Running => Volatile.Read(ref _session) != null;
    public int CurrentLine
    {
        get
        {
            if (!Enabled || Volatile.Read(ref _session) is not { } session) return 0;
            var snapshot = session.Position.Snapshot;
            if (snapshot < 0) return 0;
            var function = (int)(snapshot >> 32);
            var pc = (int)(snapshot & uint.MaxValue);
            if (function < 0 || function >= session.Image.Functions.Count || pc < 0) return 0;
            var target = session.Image.Functions[function];
            if (target.Module != session.SourceModule) return 0;
            var line = target.LineAt(pc);
            return line > 0 && line <= session.SourceLines ? line : 0;
        }
    }

    internal EcxExecutionPosition Attach(EcxImage image, string source)
    {
        var position = new EcxExecutionPosition();
        Volatile.Write(ref _session, new Session(image, position, source.Count(c => c == '\n') + 1, image.Functions[image.Entry].Module));
        return position;
    }
    internal void Clear() => Volatile.Write(ref _session, null);
}
