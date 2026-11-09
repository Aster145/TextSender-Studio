using System.Text.RegularExpressions;

namespace TextSenderStudio;

public sealed record EditorCompletion(string Text, string Description)
{
    public override string ToString() => Text + "    " + Description;
}

public sealed record SyntaxExample(string Title, string Description, string Code);

/// <summary>Editor hints for commands actually implemented by Studio's EasyCon adapter.</summary>
public static class EditorSyntax
{
    public const int IndentSize = 4;
    private static readonly Regex Definitions = new(@"(?im)^\s*FUNC\s+([\p{L}_][\p{L}\p{Nd}_]*)\s*\(", RegexOptions.CultureInvariant);
    public static IReadOnlyList<EditorCompletion> Commands { get; } = [
        new("WAIT", "等待 · WAIT 1000（毫秒）"), new("CALL", "调用项目函数 · CALL 函数名"),
        new("SEND_TEXT", "输入文字 · SEND_TEXT \"Hello\""), new("KEYBOARD", "键盘 · KEYBOARD \"ENTER\", 100"),
        new("PAD_STATE", "Studio 完整手柄状态 · 按键, 左X, 左Y, 右X, 右Y（0–255）"),
        new("HTTP_GET", "GET 请求 · HTTP_GET \"完整 URL\""),
        new("HTTP_POST", "POST JSON · HTTP_POST \"完整 URL\", \"正文\""),
        new("HTTP_REQUEST", "自定义 HTTP · 方法, 完整 URL, 正文, Content-Type"),
        new("IF", "条件开始"), new("ELIF", "其他条件"), new("ELSE", "否则"), new("ENDIF", "条件结束"),
        new("FOR", "重复 · FOR 3"), new("WHILE", "条件为真时循环"), new("UNTIL", "循环直到条件为真"), new("NEXT", "循环结束"), new("END", "结束当前代码块"), new("BREAK", "退出循环"), new("CONTINUE", "下一轮循环"),
        new("FUNC", "定义函数 · FUNC 提醒()"), new("ENDFUNC", "函数结束"), new("RETURN", "从函数返回"),
        new("PRINT", "写入执行日志"), new("ALERT", "发送设置中启用的通知"), new("BEEP", "提示音 · BEEP 800, 200"),
        new("A", "手柄 A · A 100"), new("B", "手柄 B"), new("X", "手柄 X"), new("Y", "手柄 Y"),
        new("L", "左肩键"), new("R", "右肩键"), new("ZL", "左扳机"), new("ZR", "右扳机"),
        new("HOME", "HOME 键"), new("PLUS", "+ 键"), new("MINUS", "− 键"), new("CAPTURE", "截图键"),
        new("UP", "方向上"), new("DOWN", "方向下 / 按住"), new("LEFT", "方向左"), new("RIGHT", "方向右"),
        new("LS", "左摇杆 · LS UP / LS RESET"), new("RS", "右摇杆"), new("LCLICK", "左摇杆按下"), new("RCLICK", "右摇杆按下"),
        new("RESET", "摇杆回中")
    ];
    public static IReadOnlyList<SyntaxExample> Examples { get; } = [
        new("等待与手柄", "时长单位都是毫秒；WAIT 0 不等待。按键后跟时长。", "# 等待一秒，再按 A 100 毫秒\nWAIT 1000\nA 100\nHOME 100"),
        new("键盘与文字", "字符串使用引号；组合键用 + 连接。", "SEND_TEXT \"Hello\"\nKEYBOARD \"ENTER\", 100\nKEYBOARD \"CTRL+A\", 100"),
        new("同时按键", "DOWN 持续按住，UP 释放；摇杆用 RESET 回中。", "L DOWN\nR DOWN\nWAIT 200\nL UP\nR UP\nLS UP\nWAIT 100\nLS RESET"),
        new("录制的手柄状态", "PAD_STATE 是 Studio 扩展，用于准确回放组合键和模拟摇杆幅度。坐标 0–255，中心为 128；空按键配合中心坐标释放全部输入。", "PAD_STATE \"A+LCLICK+UPLEFT\", 160, 96, 128, 128\nWAIT 200\nPAD_STATE \"\", 128, 128, 128, 128"),
        new("条件与循环", "变量以 $ 开头，# 表示注释；FOR / NEXT 包围循环。", "$count = 0\nFOR 3\n    $count = $count + 1\n    WAIT 100\nNEXT\nIF $count == 3\n    PRINT 完成\nELSE\n    PRINT 尚未完成\nENDIF"),
        new("图像标签", "@标签名 返回 0–100 的匹配分数；先在主标签开始画面预览。将示例标签替换成已保存的标签名。", "$score = @示例标签\nIF $score >= 90\n    PRINT 已匹配\nELSE\n    PRINT 未匹配\nENDIF"),
        new("直接发送 HTTP", "在脚本中填写完整 http:// 或 https:// 地址即可，不使用设置中的机器人地址，也不必先保存 HTTP 动作。POST 默认发送 JSON；自定义方法、正文类型用 HTTP_REQUEST。以下 IP、路径和正文请换成目标设备实际接口。", "HTTP_GET \"http://192.168.1.50/api/cmd?cmd=shortpress\"\nHTTP_POST \"http://192.168.1.50/api/action\", '{\"cmd\":\"shortpress\"}'\nHTTP_REQUEST \"POST\", \"http://192.168.1.50/api/action\", \"cmd=shortpress\", \"application/x-www-form-urlencoded\""),
        new("项目函数", "常用短语、启动脚本、已保存的 HTTP 动作和声音可通过函数名调用。HTTP 也可直接在脚本中写完整 URL。以下名称需要替换为当前项目中的真实函数。", "CALL phrase_hello\nCALL start_tools\nCALL robot_shortpress\nCALL reminder"),
        new("脚本内函数", "函数名支持中文；双击函数名跳转到 FUNC 定义，项目函数则打开对应编辑位置。", "FUNC 提醒()\n    PRINT 准备就绪\nENDFUNC\nCALL 提醒"),
        new("声音与通知", "BEEP 在电脑播放提示音；ALERT 使用设置中启用的推送服务。自定义音频可创建声音函数后 CALL 调用。", "BEEP 800, 200\nALERT 操作完成")
    ];

    public static IReadOnlyList<(string Name, int Position)> FindDefinitions(string text) =>
        Definitions.Matches(text).Select(m => (m.Groups[1].Value, m.Groups[1].Index)).ToArray();

    public static bool IsTokenCharacter(char value) => char.IsLetterOrDigit(value) || value is '_' or '@' or '$';

    public static (int Start, int End) TokenAt(string text, int position)
    {
        position = Math.Clamp(position, 0, text.Length);
        int start = position, end = position;
        while (start > 0 && IsTokenCharacter(text[start - 1])) start--;
        while (end < text.Length && IsTokenCharacter(text[end])) end++;
        return (start, end);
    }

    public static bool InCommentOrString(string text, int position)
    {
        int start = position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
        char quote = '\0'; bool escaped = false;
        for (int i = start; i < position; i++)
        {
            char c = text[i];
            if (escaped) { escaped = false; continue; }
            if (quote != '\0' && c == '\\') { escaped = true; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c == '#') return true;
            if (c is '"' or '\'') quote = c;
        }
        return quote != '\0';
    }

    public static IReadOnlyList<EditorCompletion> Complete(string text, int caret, IEnumerable<EditorCompletion> resources)
    {
        if (InCommentOrString(text, caret)) return [];
        var (start, _) = TokenAt(text, caret);
        string prefix = text[start..caret];
        int lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        string before = text[lineStart..start].Trim();
        var functions = FindDefinitions(text).Select(d => new EditorCompletion(d.Name, "脚本内函数"));
        IEnumerable<EditorCompletion> source = resources.Concat(functions);
        if (prefix.StartsWith('@')) source = resources.Where(c => c.Text.StartsWith('@'));
        else if (before.Equals("CALL", StringComparison.OrdinalIgnoreCase)) source = source.Where(c => !c.Text.StartsWith('@'));
        else if (prefix.StartsWith('$')) source = Regex.Matches(text, @"\$[\p{L}_][\p{L}\p{N}_]*").Select(m => new EditorCompletion(m.Value, "变量"));
        else source = Commands.Concat(source);
        return source.Where(c => c.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(c => c.Text, StringComparer.Ordinal).Take(80).ToArray();
    }

    public static string LeadingWhitespace(string line) => new(line.TakeWhile(c => c is ' ' or '\t').ToArray());
    public static bool OpensBlock(string line) => Regex.IsMatch(line.TrimStart(), @"^(IF|ELIF|ELSE|FOR|WHILE|UNTIL|FUNC|STRUCT)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool ClosesBlock(string line) => Regex.IsMatch(line.TrimStart(), @"^(ENDIF|ENDFUNC|NEXT|END|ELIF|ELSE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string RemoveIndent(string whitespace) => whitespace.EndsWith('\t') ? whitespace[..^1] : whitespace[..Math.Max(0, whitespace.Length - IndentSize)];
}
