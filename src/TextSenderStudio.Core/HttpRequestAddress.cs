namespace TextSenderStudio.Core;

/// <summary>Address rules shared by the HTTP editor and action executor.</summary>
public static class HttpRequestAddress
{
    public static void ValidateTarget(string target)
    {
        CheckCharacters(target);
        if (target.StartsWith('/'))
        {
            if (target.StartsWith("//") || !Uri.IsWellFormedUriString(target, UriKind.Relative))
                throw new ArgumentException("相对路径须以单个 / 开头，例如 /api/cmd?cmd=shortpress。");
        }
        else _ = Absolute(target, "请求地址");
    }

    public static Uri Resolve(HttpStep step, AppSettings settings)
    {
        ValidateTarget(step.Path);
        if (!step.Path.StartsWith('/')) return Absolute(step.Path, "请求地址");
        if (string.IsNullOrWhiteSpace(settings.RobotHost))
            throw new ArgumentException("请为请求填写完整 HTTP / HTTPS URL；旧的 /路径 写法需要在设置中填写默认 HTTP 设备。");
        var baseUri = Absolute(settings.RobotBaseUrl, "默认 HTTP 设备地址");
        // /path is rooted at the selected device, matching normal URL semantics.
        return new Uri(baseUri, step.Path);
    }

    private static Uri Absolute(string value, string field)
    {
        CheckCharacters(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host) || !uri.IsWellFormedOriginalString())
            throw new ArgumentException(field + "须为完整 HTTP / HTTPS URL，可包含主机名和端口。");
        return uri;
    }

    private static void CheckCharacters(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)
            || value.Contains('\\') || value.Contains('#'))
            throw new ArgumentException("请求地址不能为空，不能包含空白、反斜杠或 # 片段；需要这些字符时请使用 URL 编码。");
    }
}
