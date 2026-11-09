using System.Runtime.InteropServices;

namespace TextSenderStudio.Core;

/// <summary>Read-only Windows XInput polling. HOME/CAPTURE are not exposed by standard XInput.</summary>
public static class XInputReader
{
    public static bool TryRead(int index, out XInputSnapshot snapshot)
    {
        snapshot = default;
        if (!OperatingSystem.IsWindows() || index is < 0 or > 3) return false;
        try
        {
            if (GetState((uint)index, out var state) != 0) return false;
            snapshot = new(state.Buttons, state.LeftTrigger, state.RightTrigger, state.LX, state.LY, state.RX, state.RY);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetState(uint index, out NativeState state);
}
