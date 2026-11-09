using TextSenderStudio.Core;
using TextSenderStudio.Core.Scripting;

internal static class ChineseFunctionChecks
{
    public static async Task Run(ProjectStore store, Action<bool, string> check)
    {
        const string directory = "中文函数测试";
        var project = store.Create(directory);
        project.Phrases.Add(new() { Name = "文本", FunctionName = "输入文字", Text = "示例" });
        project.Shortcuts.Add(new() { Name = "脚本", FunctionName = "启动_工具2", Path = "scripts/missing.cmd" });
        project.HttpActions.AddRange([
            new() { Name = "", FunctionName = "HTTP短按", Steps = [] },
            new() { Name = "同一说明", FunctionName = "动作一", Steps = [] },
            new() { Name = "同一说明", FunctionName = "动作二", Steps = [] }
        ]);
        project.Sounds.Add(new() { Name = "声音", FunctionName = "_播放完成提示" });
        store.Save(directory, project);
        var restored = store.Load(directory);
        check(restored.Phrases[0].FunctionName == "输入文字"
            && restored.Shortcuts[0].FunctionName == "启动_工具2"
            && restored.Sounds[0].FunctionName == "_播放完成提示", "Chinese function names survive project save and reload for every resource type");
        check(restored.HttpActions.Select(a => a.Name).SequenceEqual(["", "同一说明", "同一说明"]), "HTTP descriptions may be empty or repeated while function names identify actions");

        void Reject(StudioProject invalid, string message)
        {
            try { store.Save(directory, invalid); }
            catch (ArgumentException) { check(true, message); return; }
            throw new Exception("FAIL: " + message);
        }
        var duplicate = ProjectStore.Clone(restored);
        duplicate.HttpActions[1].FunctionName = "输入文字";
        Reject(duplicate, "Chinese callable names remain unique across resource categories");
        foreach (var name in new[] { "9动作", "含 空格", "动作-一", "WAIT", "if", "HOME", "Call", "HTTP_GET" })
        {
            var invalid = ProjectStore.Clone(restored); invalid.HttpActions[0].FunctionName = name;
            Reject(invalid, "invalid or reserved function name cannot be saved: " + name);
        }
        var blankPhrase = ProjectStore.Clone(restored); blankPhrase.Phrases[0].Name = "";
        Reject(blankPhrase, "HTTP optional descriptions do not relax phrase display names");
        var duplicateSound = ProjectStore.Clone(restored);
        duplicateSound.Sounds.Add(new() { Name = "声音", FunctionName = "另外声音" });
        Reject(duplicateSound, "HTTP duplicate descriptions do not relax sound display names");
        check(store.Load(directory).HttpActions[0].FunctionName == "HTTP短按", "invalid edits preserve saved Chinese action names");

        var bridge = new FakeBridge();
        string? played = null;
        var actions = new ProjectActions(new AppSettings { SendDelayMs = 0 }, restored, store.ProjectPath(directory), bridge,
            (_, _) => Task.FromResult(0.0), _ => { }, (sound, _) => { played = sound.FunctionName; return Task.CompletedTask; });
        await new StudioScriptEngine().RunAsync("CALL 输入文字\nCALL HTTP短按\nCALL _播放完成提示", actions, CancellationToken.None);
        check(bridge.Texts.Count == 1 && bridge.Texts[0].Item1.StartsWith("示例") && played == "_播放完成提示", "Chinese CALL resolves restored phrase HTTP and sound resources using simulated input and audio");
        try { await actions.CallAsync("启动_工具2", CancellationToken.None); }
        catch (FileNotFoundException) { check(true, "Chinese shortcut resolves its configured path without launching missing process"); return; }
        throw new Exception("FAIL: Chinese shortcut did not resolve to the fixture path");
    }
}
