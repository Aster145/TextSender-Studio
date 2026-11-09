using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using TextSenderStudio.Core.Scripting;

namespace TextSenderStudio.Core;

public sealed class ProjectStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public string ProjectsRoot => Path.Combine(Root, "projects");
    public ProjectStore(string root) { Root = Path.GetFullPath(root); Directory.CreateDirectory(ProjectsRoot); }
    public AppSettings LoadSettings() => Read<AppSettings>(Path.Combine(Root, "settings.json")) ?? new();
    public void SaveSettings(AppSettings settings) { ValidateSettings(settings); Write(Path.Combine(Root, "settings.json"), settings); }
    public static void ValidateSettings(AppSettings s)
    {
        PushNotificationDispatcher.Validate(s.PushNotifications);
        if (!Uri.TryCreate(s.EspBaseUrl, UriKind.Absolute, out var esp) || esp.Host.Length == 0 || s.EspHost.Contains('/')) throw new ArgumentException("ESP32 地址无效。请仅填写 IP 或主机名。");
        if (s.RobotHost.Length > 0 && (!Uri.TryCreate(s.RobotBaseUrl, UriKind.Absolute, out var robot) || robot.Host.Length == 0 || s.RobotHost.Contains('/'))) throw new ArgumentException("机器人地址无效。");
        if (s.RobotScheme is not ("http" or "https")) throw new ArgumentException("机器人协议须为 HTTP 或 HTTPS。");
        if (new[] { s.EspPort, s.RobotPort, s.LanPort }.Any(p => p < 1 || p > 65535)) throw new ArgumentException("端口范围为 1–65535。");
        if (s.RequestTimeoutMs < 1 || s.RequestTimeoutMs > 600000 || s.SendDelayMs < 0 || s.CharacterDelayMs < 0 || s.LineDelayMs < 0) throw new ArgumentException("等待必须为非负毫秒；超时范围为 1–600000 ms。");
    }
    public IReadOnlyList<string> ListProjects() => Directory.EnumerateDirectories(ProjectsRoot).Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0 && File.Exists(Path.Combine(p, "project.json"))).Select(p => Path.GetFileName(p)!).Order(StringComparer.CurrentCulture).ToArray();
    public string ProjectPath(string directory)
    {
        ValidateDirectoryName(directory);
        var path = Path.Combine(ProjectsRoot, directory);
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("项目目录不能是链接。");
        return path;
    }
    public static void ValidateDirectoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name is "." or ".." || Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) throw new ArgumentException("请输入有效的项目文件夹名称。");
    }
    public StudioProject Create(string name)
    {
        var path = ProjectPath(name);
        if (Directory.Exists(path)) throw new IOException("同名项目已经存在。");
        Directory.CreateDirectory(path); Directory.CreateDirectory(Path.Combine(path, "scripts")); Directory.CreateDirectory(Path.Combine(path, "labels"));
        var project = new StudioProject { Name = name };
        Save(name, project);
        File.WriteAllText(Resolve(name, "main.ecs"), "# 等待单位：毫秒\nWAIT 500\nPRINT 准备就绪\n", new UTF8Encoding(false));
        return project;
    }
    public StudioProject Load(string directory) => Read<StudioProject>(Path.Combine(ProjectPath(directory), "project.json")) ?? throw new IOException("项目配置不存在。");
    public void Save(string directory, StudioProject project)
    {
        ValidateFunctions(project); Write(Path.Combine(ProjectPath(directory), "project.json"), project);
    }
    public void DeleteToRecycleBin(string directory) => FileSystem.DeleteDirectory(ProjectPath(directory), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
    public string Resolve(string directory, string relative)
    {
        var root = ProjectPath(directory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("文件必须保存在当前项目中。");
        for (var current = Path.GetDirectoryName(path); current != null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("项目文件路径不能包含链接目录。");
        return path;
    }
    public static void ValidateFunctions(StudioProject project)
    {
        var names = project.Phrases.Select(p => p.FunctionName).Concat(project.Shortcuts.Select(p => p.FunctionName)).Concat(project.HttpActions.Select(p => p.FunctionName)).Concat(project.Sounds.Select(p => p.FunctionName)).ToArray();
        foreach (var name in names) ScriptFunctionNames.Validate(name);
        // HTTP Name is an optional description; FunctionName is its unique identifier.
        foreach (var labels in new[] { project.Phrases.Select(p => p.Name), project.Shortcuts.Select(p => p.Name), project.Labels.Select(p => p.Name), project.Sounds.Select(p => p.Name) })
            if (labels.Any(string.IsNullOrWhiteSpace) || labels.Distinct(StringComparer.Ordinal).Count() != labels.Count()) throw new ArgumentException("同类条目的显示名称不能为空或重复。");
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("同一项目中的函数名不能重复（不区分字母大小写）。");
        foreach (var sound in project.Sounds)
        {
            if (sound.Source is not ("System" or "File")) throw new ArgumentException("声音来源须为系统提示音或音乐文件。");
            if (sound.Source == "System" && !new[] { "Asterisk", "Beep", "Exclamation", "Hand", "Question" }.Contains(sound.SystemSound)) throw new ArgumentException("系统提示音无效。");
            if (sound.Source == "File" && string.IsNullOrWhiteSpace(sound.FilePath)) throw new ArgumentException("请为声音提醒选择音乐文件。");
        }
    }
    public static T? Read<T>(string path) => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), Json) : default;
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}
