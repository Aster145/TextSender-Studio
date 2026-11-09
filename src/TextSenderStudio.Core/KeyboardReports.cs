#nullable disable
namespace TextSenderStudio.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

using System.Text;
using System.Threading;

// Immutable USB boot-keyboard state: eight modifiers and at most six ordinary keys.
public sealed class KeyboardReport
{
    readonly byte[] keys = new byte[6];
    public byte Modifiers { get; private set; }
    public byte[] Keys { get { return (byte[])keys.Clone(); } }
    public bool IsEmpty {
        get {
            if (Modifiers != 0) return false;
            foreach (byte key in keys) if (key != 0) return false;
            return true;
        }
    }

    public KeyboardReport(byte modifiers, params byte[] usages)
    {
        if (usages == null) throw new ArgumentNullException("usages");
        if (usages.Length > 6) throw new ArgumentException("USB 键盘最多同时按下 6 个普通键。", "usages");
        Modifiers = modifiers;
        var seen = new HashSet<byte>();
        for (int index = 0; index < usages.Length; index++) {
            byte usage = usages[index];
            if (usage != 0 && (usage < 4 || usage >= 224 || !seen.Add(usage)))
                throw new ArgumentException("USB 普通键报告包含无效或重复键。", "usages");
            keys[index] = usage;
        }
    }

    internal string Command()
    {
        var value = new StringBuilder("HID ");
        value.Append(Modifiers.ToString(CultureInfo.InvariantCulture));
        foreach (byte key in keys) value.Append(' ').Append(key.ToString(CultureInfo.InvariantCulture));
        return value.ToString();
    }
}

public sealed class KeyboardReportState
{
    // Store physical identities, including rejected seventh keys, until their key-up.
    // A rejected key never becomes a delayed press when another slot becomes free.
    readonly Dictionary<int, byte> down = new Dictionary<int, byte>();
    readonly List<byte> ordinary = new List<byte>();

    public KeyboardReport Clear()
    {
        down.Clear(); ordinary.Clear();
        return new KeyboardReport(0);
    }

    public bool HandleKeyMessage(int message, IntPtr wParam, IntPtr lParam,
                                out KeyboardReport report, out string warning)
    {
        report = null; warning = null;
        bool pressed = message == 0x100 || message == 0x104;
        if (!pressed && message != 0x101 && message != 0x105) return false;
        long flags = lParam.ToInt64();
        int virtualKey = (int)wParam.ToInt64();
        int scan = (int)((flags >> 16) & 255);
        bool extended = (flags & 0x01000000L) != 0;
        // Bit 30 also suppresses held keys after focus was lost and Clear was called.
        if (pressed && (flags & 0x40000000L) != 0) return false;
        byte usage = MapKey(virtualKey, scan, extended);
        if (usage == 0) {
            if (pressed) warning = "这个按键不属于支持的标准 USB 键盘按键。";
            return false;
        }
        // Pause and NumLock may share a scan code; their VK distinguishes them.
        int identity = (virtualKey == 0x13 || virtualKey == 0x90) ? 0x20000 + virtualKey :
            (scan != 0 ? scan | (extended ? 0x100 : 0) : 0x10000 + virtualKey);
        byte previous;
        if (pressed) {
            if (down.ContainsKey(identity)) return false;
            if (usage < 224 && !ordinary.Contains(usage) && ordinary.Count == 6) {
                down.Add(identity, 0);
                warning = "最多同时按下 6 个普通键，另支持 8 个左右修饰键；超出的键本次未发送，请松开后重按。";
                return false;
            }
            down.Add(identity, usage);
            if (usage < 224 && !ordinary.Contains(usage)) ordinary.Add(usage);
        } else {
            if (!down.TryGetValue(identity, out previous)) return false;
            down.Remove(identity);
            if (previous == 0) return false;
            if (previous < 224 && !down.ContainsValue(previous)) ordinary.Remove(previous);
        }
        byte modifiers = 0;
        foreach (byte held in down.Values)
            if (held >= 224) modifiers |= (byte)(1 << (held - 224));
        report = new KeyboardReport(modifiers, ordinary.ToArray());
        return true;
    }

    // Windows Set-1 scan codes preserve key positions when the PC layout changes.
    // USB usages follow HID Usage Tables, Keyboard/Keypad page (0x07).
    static byte MapKey(int vk, int scan, bool extended)
    {
        if (vk == 0x13) return 0x48; // Pause
        if (vk == 0x2C) return 0x46; // PrintScreen
        if (vk == 0x90) return 0x53; // NumLock
        if (vk == 0x91) return 0x47; // ScrollLock
        if (vk == 0xA0) return 0xE1;
        if (vk == 0xA1) return 0xE5;
        if (vk == 0xA2) return 0xE0;
        if (vk == 0xA3) return 0xE4;
        if (vk == 0xA4) return 0xE2;
        if (vk == 0xA5) return 0xE6;
        if (vk == 0x10) return scan == 0x36 ? (byte)0xE5 : (byte)0xE1;
        if (vk == 0x11) return extended ? (byte)0xE4 : (byte)0xE0;
        if (vk == 0x12) return extended ? (byte)0xE6 : (byte)0xE2;
        if (vk == 0x5B) return 0xE3;
        if (vk == 0x5C) return 0xE7;
        if (vk >= 0x7C && vk <= 0x87) return (byte)(0x68 + vk - 0x7C); // F13..F24
        if (extended) {
            switch (scan) {
                case 0x1C: return 0x58; // keypad Enter
                case 0x35: return 0x54; // keypad /
                case 0x47: return 0x4A; // Home
                case 0x48: return 0x52; // Up
                case 0x49: return 0x4B; // PageUp
                case 0x4B: return 0x50; // Left
                case 0x4D: return 0x4F; // Right
                case 0x4F: return 0x4D; // End
                case 0x50: return 0x51; // Down
                case 0x51: return 0x4E; // PageDown
                case 0x52: return 0x49; // Insert
                case 0x53: return 0x4C; // Delete
                case 0x5D: return 0x65; // Application/Menu
            }
            // Do not confuse browser/media E0 scan codes with letter positions.
            return MapVirtualKey(vk, extended);
        }
        if (scan >= 0x02 && scan <= 0x0A) return (byte)(0x1E + scan - 0x02);
        if (scan >= 0x3B && scan <= 0x44) return (byte)(0x3A + scan - 0x3B);
        switch (scan) {
            case 0x01: return 0x29; case 0x0B: return 0x27;
            case 0x0C: return 0x2D; case 0x0D: return 0x2E;
            case 0x0E: return 0x2A; case 0x0F: return 0x2B;
            case 0x10: return 0x14; case 0x11: return 0x1A; case 0x12: return 0x08;
            case 0x13: return 0x15; case 0x14: return 0x17; case 0x15: return 0x1C;
            case 0x16: return 0x18; case 0x17: return 0x0C; case 0x18: return 0x12;
            case 0x19: return 0x13; case 0x1A: return 0x2F; case 0x1B: return 0x30;
            case 0x1C: return 0x28; case 0x1D: return 0xE0;
            case 0x1E: return 0x04; case 0x1F: return 0x16; case 0x20: return 0x07;
            case 0x21: return 0x09; case 0x22: return 0x0A; case 0x23: return 0x0B;
            case 0x24: return 0x0D; case 0x25: return 0x0E; case 0x26: return 0x0F;
            case 0x27: return 0x33; case 0x28: return 0x34; case 0x29: return 0x35;
            case 0x2A: return 0xE1; case 0x2B: return 0x31;
            case 0x2C: return 0x1D; case 0x2D: return 0x1B; case 0x2E: return 0x06;
            case 0x2F: return 0x19; case 0x30: return 0x05; case 0x31: return 0x11;
            case 0x32: return 0x10; case 0x33: return 0x36; case 0x34: return 0x37;
            case 0x35: return 0x38; case 0x36: return 0xE5; case 0x37: return 0x55;
            case 0x38: return 0xE2; case 0x39: return 0x2C; case 0x3A: return 0x39;
            case 0x45: return 0x53; case 0x46: return 0x47;
            case 0x47: return 0x5F; case 0x48: return 0x60; case 0x49: return 0x61;
            case 0x4A: return 0x56; case 0x4B: return 0x5C; case 0x4C: return 0x5D;
            case 0x4D: return 0x5E; case 0x4E: return 0x57; case 0x4F: return 0x59;
            case 0x50: return 0x5A; case 0x51: return 0x5B; case 0x52: return 0x62;
            case 0x53: return 0x63; case 0x56: return 0x64;
            case 0x57: return 0x44; case 0x58: return 0x45;
        }
        return MapVirtualKey(vk, extended);
    }

    static byte MapVirtualKey(int vk, bool extended)
    {
        if (vk >= 0x41 && vk <= 0x5A) return (byte)(4 + vk - 0x41);
        if (vk >= 0x31 && vk <= 0x39) return (byte)(0x1E + vk - 0x31);
        if (vk >= 0x70 && vk <= 0x7B) return (byte)(0x3A + vk - 0x70);
        if (vk >= 0x61 && vk <= 0x69) return (byte)(0x59 + vk - 0x61);
        switch (vk) {
            case 0x30: return 0x27; case 0x08: return 0x2A; case 0x09: return 0x2B;
            case 0x0D: return extended ? (byte)0x58 : (byte)0x28;
            case 0x14: return 0x39; case 0x1B: return 0x29; case 0x20: return 0x2C;
            case 0x21: return 0x4B; case 0x22: return 0x4E; case 0x23: return 0x4D;
            case 0x24: return 0x4A; case 0x25: return 0x50; case 0x26: return 0x52;
            case 0x27: return 0x4F; case 0x28: return 0x51; case 0x2D: return 0x49;
            case 0x2E: return 0x4C; case 0x5D: return 0x65; case 0x60: return 0x62;
            case 0x6A: return 0x55; case 0x6B: return 0x57; case 0x6C: return 0x85;
            case 0x6D: return 0x56; case 0x6E: return 0x63; case 0x6F: return 0x54;
            case 0xBA: return 0x33; case 0xBB: return 0x2E; case 0xBC: return 0x36;
            case 0xBD: return 0x2D; case 0xBE: return 0x37; case 0xBF: return 0x38;
            case 0xC0: return 0x35; case 0xDB: return 0x2F; case 0xDC: return 0x31;
            case 0xDD: return 0x30; case 0xDE: return 0x34; case 0xE2: return 0x64;
        }
        return 0;
    }
}
