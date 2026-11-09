namespace TextSenderStudio.Core;

public sealed class DirectControlSettings
{
    public Dictionary<string, int> KeyBindings { get; set; } = ControllerCatalog.DefaultBindings();
    public bool KeyboardEnabled { get; set; } = true;
    public bool GamepadEnabled { get; set; }
    public bool SwapFaceButtons { get; set; } = true;
    public int GamepadIndex { get; set; }
    public int DeadZone { get; set; } = 7849;
    public byte TriggerThreshold { get; set; } = 25;
}

public sealed record ControllerTarget(string Name, string Label, string Group);

public static class ControllerCatalog
{
    // Wire button order is shared with InputBridge and the ESP32 USB report.
    public static IReadOnlyList<string> ButtonNames { get; } = ["Y", "B", "A", "X", "L", "R", "ZL", "ZR", "MINUS", "PLUS", "LCLICK", "RCLICK", "HOME", "CAPTURE"];
    public static IReadOnlyList<string> Directions { get; } = ["UP", "UPRIGHT", "RIGHT", "DOWNRIGHT", "DOWN", "DOWNLEFT", "LEFT", "UPLEFT"];
    public static IReadOnlyList<ControllerTarget> Targets { get; } = BuildTargets();

    private static ControllerTarget[] BuildTargets()
    {
        var targets = ButtonNames.Select(n => new ControllerTarget(n, n switch { "MINUS" => "−", "PLUS" => "+", "LCLICK" => "左摇杆按下", "RCLICK" => "右摇杆按下", _ => n }, "按键")).ToList();
        targets.AddRange(Directions.Select(n => new ControllerTarget(n, n, "方向键")));
        foreach (string stick in new[] { "LS", "RS" })
            targets.AddRange(Directions.Append("RESET").Select(n => new ControllerTarget(stick + "_" + n, n, stick == "LS" ? "左摇杆" : "右摇杆")));
        return targets.ToArray();
    }

    public static Dictionary<string, int> DefaultBindings()
    {
        var result = Targets.ToDictionary(t => t.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in new Dictionary<string, int> {
            ["A"] = 76, ["B"] = 75, ["X"] = 73, ["Y"] = 74, ["L"] = 71, ["R"] = 84,
            ["ZL"] = 70, ["ZR"] = 82, ["PLUS"] = 107, ["MINUS"] = 109, ["CAPTURE"] = 90,
            ["HOME"] = 67, ["LCLICK"] = 81, ["RCLICK"] = 69,
            ["LS_UP"] = 87, ["LS_DOWN"] = 83, ["LS_LEFT"] = 65, ["LS_RIGHT"] = 68,
            ["RS_UP"] = 38, ["RS_DOWN"] = 40, ["RS_LEFT"] = 37, ["RS_RIGHT"] = 39 }) result[pair.Key] = pair.Value;
        return result;
    }
}

public readonly record struct PadState(ushort Buttons, byte Hat, byte LX, byte LY, byte RX, byte RY)
{
    public static PadState Neutral => new(0, 15, 128, 128, 128, 128);
    public string ButtonNames
    {
        get
        {
            ushort buttons = Buttons;
            var names = ControllerCatalog.ButtonNames.Where((_, i) => (buttons & (1 << i)) != 0)
                .Select(n => n == "LCLICK" ? "L3" : n == "RCLICK" ? "R3" : n).ToList();
            var (x, y) = ControllerMapper.HatAxes(Hat);
            if (x != 0) names.Add(x < 0 ? "LEFT" : "RIGHT");
            if (y != 0) names.Add(y < 0 ? "UP" : "DOWN");
            return string.Join('+', names);
        }
    }
    public static PadState Merge(PadState first, PadState second)
    {
        var a = ControllerMapper.HatAxes(first.Hat); var b = ControllerMapper.HatAxes(second.Hat);
        static byte Axis(byte x, byte y)
        {
            static double Unit(byte value) => (value - 128.0) / (value < 128 ? 128 : 127);
            double sum = Math.Clamp(Unit(x) + Unit(y), -1, 1);
            return (byte)(128 + Math.Round(sum * (sum < 0 ? 128 : 127)));
        }
        return new((ushort)(first.Buttons | second.Buttons), ControllerMapper.HatFromAxes(a.X + b.X, a.Y + b.Y),
            Axis(first.LX, second.LX), Axis(first.LY, second.LY), Axis(first.RX, second.RX), Axis(first.RY, second.RY));
    }
}

public readonly record struct XInputSnapshot(ushort Buttons, byte LeftTrigger, byte RightTrigger, short LX, short LY, short RX, short RY);

public static class ControllerMapper
{
    public static PadState FromKeys(IEnumerable<int> virtualKeys, DirectControlSettings settings)
    {
        var held = virtualKeys.ToHashSet();
        return FromTargets((settings.KeyBindings ?? []).Where(p => p.Value != 0 && held.Contains(p.Value)).Select(p => p.Key));
    }

    public static PadState FromTargets(IEnumerable<string> targets)
    {
        var active = targets.Select(t => t.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        ushort buttons = 0;
        for (int i = 0; i < ControllerCatalog.ButtonNames.Count; i++) if (active.Contains(ControllerCatalog.ButtonNames[i])) buttons |= (ushort)(1 << i);
        static (int X, int Y) Axes(HashSet<string> names, string prefix)
        {
            // Direction unions preserve one axis when opposing directions cancel the other.
            bool left = false, right = false, up = false, down = false;
            foreach (string name in ControllerCatalog.Directions)
                if (names.Contains(prefix + name)) { left |= name.Contains("LEFT"); right |= name.Contains("RIGHT"); up |= name.Contains("UP"); down |= name.Contains("DOWN"); }
            return ((right ? 1 : 0) - (left ? 1 : 0), (down ? 1 : 0) - (up ? 1 : 0));
        }
        static byte Axis(int direction) => direction < 0 ? (byte)0 : direction > 0 ? (byte)255 : (byte)128;
        var hat = Axes(active, ""); var ls = active.Contains("LS_RESET") ? (0, 0) : Axes(active, "LS_");
        var rs = active.Contains("RS_RESET") ? (0, 0) : Axes(active, "RS_");
        return new(buttons, HatFromAxes(hat.X, hat.Y), Axis(ls.Item1), Axis(ls.Item2), Axis(rs.Item1), Axis(rs.Item2));
    }

    public static PadState FromXInput(XInputSnapshot source, DirectControlSettings settings)
    {
        var targets = new List<string>();
        void Button(int bit, string name) { if ((source.Buttons & bit) != 0) targets.Add(name); }
        Button(0x1000, settings.SwapFaceButtons ? "B" : "A"); Button(0x2000, settings.SwapFaceButtons ? "A" : "B");
        Button(0x4000, settings.SwapFaceButtons ? "Y" : "X"); Button(0x8000, settings.SwapFaceButtons ? "X" : "Y");
        Button(0x100, "L"); Button(0x200, "R"); Button(0x40, "LCLICK"); Button(0x80, "RCLICK");
        Button(0x10, "PLUS"); Button(0x20, "MINUS"); Button(1, "UP"); Button(2, "DOWN"); Button(4, "LEFT"); Button(8, "RIGHT");
        if (source.LeftTrigger > settings.TriggerThreshold) targets.Add("ZL");
        if (source.RightTrigger > settings.TriggerThreshold) targets.Add("ZR");
        var report = FromTargets(targets);
        static (byte X, byte Y) Stick(short x, short y, int deadZone)
        {
            if ((long)x * x + (long)y * y <= (long)deadZone * deadZone) return (128, 128);
            static byte Axis(int value) => (byte)Math.Clamp(value >= 0 ? 128 + (int)Math.Round(value * 127.0 / 32767) : 128 + (int)Math.Round(value * 128.0 / 32768), 0, 255);
            return (Axis(x), Axis(-((int)y)));
        }
        int zone = Math.Clamp(settings.DeadZone, 0, 32767);
        var left = Stick(source.LX, source.LY, zone); var right = Stick(source.RX, source.RY, zone);
        return report with { LX = left.X, LY = left.Y, RX = right.X, RY = right.Y };
    }

    public static (int X, int Y) HatAxes(byte hat) => hat switch { 0 => (0, -1), 1 => (1, -1), 2 => (1, 0), 3 => (1, 1), 4 => (0, 1), 5 => (-1, 1), 6 => (-1, 0), 7 => (-1, -1), _ => (0, 0) };
    public static byte HatFromAxes(int x, int y) => (Math.Sign(x), Math.Sign(y)) switch { (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7, _ => 15 };
}
