using System.Text;
using EasyCon.Script.Syntax;

namespace TextSenderStudio;

/// <summary>Changes indentation only, using the same keyword tokens as the compiler.</summary>
public static class ScriptIndentation
{
    public static ScriptIndentationResult Format(string source)
    {
        var blocks = new Stack<TokenType>();
        var result = new StringBuilder(source.Length);
        var changes = new List<ScriptIndentationResult.Change>();
        foreach (var line in ReadLines(source))
        {
            var kind = line.Kind;
            int depth = blocks.Count;
            if (kind == TokenType.ELIF || kind == TokenType.ELSE && line.OnlyToken)
            {
                if (blocks.TryPeek(out var branchBlock) && branchBlock == TokenType.IF) depth--;
            }
            else if (line.OnlyToken && IsClosing(kind) && blocks.TryPeek(out var closingBlock) && Matches(kind, closingBlock))
            {
                blocks.Pop(); depth--;
            }
            int indentation = line.InString ? line.Body - line.Start : line.Body == line.End ? 0 : depth * EditorSyntax.IndentSize;
            changes.Add(new(line.Start, line.Body - line.Start, result.Length, indentation));
            if (line.InString) result.Append(source, line.Start, line.Body - line.Start);
            else result.Append(' ', indentation);
            result.Append(source, line.Body, line.Next - line.Body);
            if (IsOpening(kind)) blocks.Push(kind);
        }
        return new ScriptIndentationResult(source.Length, result.ToString(), changes);
    }

    /// <summary>Matched block ranges in source order, sharing the formatter's lexer rules.</summary>
    public static IReadOnlyList<ScriptBlock> AnalyzeBlocks(string source)
    {
        var open = new Stack<(SourceLine Line, int Depth)>();
        var result = new List<ScriptBlock>();
        foreach (var line in ReadLines(source))
        {
            if (line.OnlyToken && IsClosing(line.Kind) && open.TryPeek(out var block)
                && Matches(line.Kind, block.Line.Kind))
            {
                open.Pop();
                result.Add(new ScriptBlock(block.Line.End, line.End, block.Line.Number, line.Number,
                    block.Depth, source[block.Line.Body..block.Line.End].TrimEnd()));
            }
            if (IsOpening(line.Kind)) open.Push((line, open.Count));
        }
        result.Sort((left, right) => left.StartOffset.CompareTo(right.StartOffset));
        return result;
    }

    private readonly record struct SourceLine(int Start, int Body, int End, int Next, int Number,
        TokenType Kind, bool OnlyToken, bool InString);

    private static IEnumerable<SourceLine> ReadLines(string source)
    {
        var tokens = SyntaxTree.ParseTokens(source);
        int tokenIndex = 0, number = 1;
        for (int start = 0; start < source.Length; number++)
        {
            int end = start;
            while (end < source.Length && source[end] is not ('\r' or '\n')) end++;
            int next = end;
            if (next < source.Length && source[next] == '\r') next++;
            if (next < source.Length && source[next] == '\n') next++;
            int body = start;
            while (body < end && source[body] is ' ' or '\t') body++;
            while (tokenIndex < tokens.Length && tokens[tokenIndex].Span.End <= start) tokenIndex++;
            var first = tokenIndex < tokens.Length ? tokens[tokenIndex] : null;

            // Escaped-newline strings retain continuation whitespace as payload.
            bool inString = first is not null && first.Type == TokenType.STRING
                && first.Span.Start < start && first.Span.End > start;
            var kind = !inString && first is not null && first.Span.Start == body ? first.Type : TokenType.BadToken;
            bool onlyToken = tokenIndex + 1 >= tokens.Length || tokens[tokenIndex + 1].Span.Start >= end
                || tokens[tokenIndex + 1].Type is TokenType.COMMENT or TokenType.NEWLINE or TokenType.EOF;
            yield return new SourceLine(start, body, end, next, number, kind, onlyToken, inString);
            start = next;
        }
    }

    private static bool IsOpening(TokenType kind) => kind is TokenType.IF or TokenType.FOR
        or TokenType.WHILE or TokenType.UNTIL or TokenType.FUNC or TokenType.STRUCT;
    private static bool IsClosing(TokenType kind) => kind is TokenType.END or TokenType.ENDIF or TokenType.NEXT or TokenType.ENDFUNC;
    private static bool Matches(TokenType close, TokenType open) => close == TokenType.END
        || close == TokenType.ENDIF && open == TokenType.IF
        || close == TokenType.NEXT && open == TokenType.FOR
        || close == TokenType.ENDFUNC && open == TokenType.FUNC;
}

/// <summary>Offsets exclude line endings; the opening header remains visible when folded.</summary>
public sealed record ScriptBlock(int StartOffset, int EndOffset, int StartLine, int EndLine, int Depth, string Header);

public sealed class ScriptIndentationResult
{
    internal readonly record struct Change(int OldStart, int OldIndent, int NewStart, int NewIndent);
    private readonly int _originalLength;
    private readonly IReadOnlyList<Change> _changes;
    internal ScriptIndentationResult(int originalLength, string text, IReadOnlyList<Change> changes)
    { _originalLength = originalLength; Text = text; _changes = changes; }
    public string Text { get; }

    public int MapPosition(int position)
    {
        position = Math.Clamp(position, 0, _originalLength);
        int delta = 0;
        foreach (var change in _changes)
        {
            if (position < change.OldStart) break;
            if (position < change.OldStart + change.OldIndent)
                return change.NewStart + Math.Min(position - change.OldStart, change.NewIndent);
            delta = change.NewStart + change.NewIndent - change.OldStart - change.OldIndent;
        }
        return Math.Clamp(position + delta, 0, Text.Length);
    }
}
