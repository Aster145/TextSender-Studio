namespace EasyCon.Script.Bytecode;

/// <summary>
/// Optional desktop observation only. The VM publishes a single atomic function/PC
/// snapshot at its existing yield boundaries and before potentially blocking host
/// calls. Reading does not suspend execution or enqueue instruction callbacks.
/// </summary>
public sealed class EcxExecutionPosition
{
    private long _position = -1;
    public long Snapshot => System.Threading.Volatile.Read(ref _position);
    internal void Capture(int function, int pc) =>
        System.Threading.Volatile.Write(ref _position, ((long)function << 32) | (uint)pc);
}
