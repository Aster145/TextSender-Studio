using TextSenderStudio.Core.Scripting;

internal static class PadStateChecks
{
    public static async Task Run(StudioScriptEngine engine, Action<bool, string> check)
    {
        var names = new[] { "A", "B", "X", "Y", "L", "R", "ZL", "ZR", "MINUS", "PLUS", "LCLICK", "RCLICK", "HOME", "CAPTURE", "UP", "DOWN", "LEFT", "RIGHT", "UPLEFT", "UPRIGHT", "DOWNLEFT", "DOWNRIGHT" };
        var expected = new[] { "A", "B", "X", "Y", "L", "R", "ZL", "ZR", "MINUS", "PLUS", "L3", "R3", "HOME", "CAPTURE", "UP", "DOWN", "LEFT", "RIGHT", "UP+LEFT", "UP+RIGHT", "DOWN+LEFT", "DOWN+RIGHT" };
        var all = new FakeActions([]);
        await engine.RunAsync(string.Join('\n', names.Select(name => name + " 0")), all, CancellationToken.None);
        check(all.Pads.SequenceEqual(expected.Select(name => name + ":0")), "all EasyCon Pro buttons and eight D-pad directions dispatch with correct aliases");

        var fake = new FakeActions([]);
        await engine.RunAsync("PAD_STATE \"A+LCLICK+UPLEFT\", 64, 192, 200, 16\nR DOWN\nA UP\nB 0\nRS RESET", fake, CancellationToken.None);
        check(fake.States[0] == "A+L3+LEFT+UP:64,192,200,16", "PAD_STATE preserves exact analogue coordinates and chord");
        check(fake.States[1] == "A+L3+LEFT+R+UP:64,192,200,16" && fake.States[2] == "L3+LEFT+R+UP:64,192,200,16", "EasyCon DOWN and UP continue from recorded full state");
        check(fake.States[3] == "B+L3+LEFT+R+UP:64,192,200,16" && fake.States[4] == fake.States[2], "tap after PAD_STATE restores preceding chord and axes");
        check(fake.States[5] == "L3+LEFT+R+UP:64,192,128,128" && fake.States[^1] == ":128,128,128,128", "stick reset after PAD_STATE preserves other axes and script end releases all input");
        check(engine.Validate("WAIT 0", ["pad_state"]).Any(d => d.IsError), "PAD_STATE is reserved for Studio's host extension");

        foreach (var source in new[]
        {
            "PAD_STATE \"A\", -1, 128, 128, 128", "PAD_STATE \"A\", 128, 256, 128, 128",
            "PAD_STATE \"A\", 128, 128, -1, 128", "PAD_STATE \"A\", 128, 128, 128, 256",
            "PAD_STATE \"C\", 128, 128, 128, 128", "PAD_STATE \"SL\", 128, 128, 128, 128",
            "PAD_STATE \"3\", 128, 128, 128, 128", "PAD_STATE \"LS\", 128, 128, 128, 128"
        })
        {
            var invalid = new FakeActions([]);
            try { await engine.RunAsync(source, invalid, CancellationToken.None); }
            catch (StudioScriptException) { check(invalid.States.Count == 0, "invalid full state is rejected before sending: " + source); continue; }
            throw new Exception("FAIL: invalid PAD_STATE was accepted");
        }
        check(engine.Validate("PAD_STATE \"A\", 128, 128").Any(d => d.IsError), "PAD_STATE requires all four axis coordinates");
    }
}
