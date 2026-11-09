using EasyCon.Script.Binding;
using EasyCon.Script.Syntax;

namespace TextSenderStudio.Core.Scripting;

/// <summary>Names accepted by both saved project actions and generated EasyCon declarations.</summary>
public static class ScriptFunctionNames
{
    private static readonly HashSet<string> BuiltIns = BuiltinFunctions.Manifest
        .Select(entry => entry.Symbol.Name)
        .Concat(["CALL", "SEND_TEXT", "KEYBOARD", "PAD_STATE", "HTTP_GET", "HTTP_POST", "HTTP_REQUEST"])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string? name) => !string.IsNullOrEmpty(name)
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    public static bool IsReserved(string? name)
    {
        if (!IsValid(name)) return false;
        if (BuiltIns.Contains(name!)) return true;
        // Ask the same lexer that compiles the script, so controller and language
        // keywords cannot accidentally become saved action names.
        var token = SyntaxTree.ParseTokens(name!.ToUpperInvariant())[0];
        return token.Type is not (TokenType.IDENT or TokenType.CONST);
    }

    public static void Validate(string? name)
    {
        if (!IsValid(name))
            throw new ArgumentException("函数名可使用中文、字母、数字和下划线，不能以数字开头，也不能包含空格或标点。");
        if (IsReserved(name))
            throw new ArgumentException($"{name} 是脚本保留名称，请使用其他函数名。");
    }
}
