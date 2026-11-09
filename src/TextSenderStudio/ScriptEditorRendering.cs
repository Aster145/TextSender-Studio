using System.Windows;
using System.Windows.Media;
using EasyCon.Script.Syntax;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace TextSenderStudio;

public enum ScriptHighlightKind { Keyword, Function, String, Comment, Number, Variable, Label }
public sealed record ScriptHighlightSpan(int Start, int End, ScriptHighlightKind Kind);

/// <summary>Uses compiler tokens, so comments and PRINT payloads cannot become fake keywords.</summary>
internal sealed class ScriptColorizer : DocumentColorizingTransformer
{
    private static readonly HashSet<string> Commands = EditorSyntax.Commands.Select(c => c.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly Brush[] BrushesByKind = [Color("#6740A4"), Color("#0C5D77"), Color("#277342"), Color("#70798A"), Color("#9E4C0D"), Color("#2459A6"), Color("#9D3972")];
    public IReadOnlyList<ScriptHighlightSpan> Spans { get; private set; } = [];

    public void Update(string source)
    {
        var spans = new List<ScriptHighlightSpan>();
        foreach (var token in SyntaxTree.ParseTokens(source))
        {
            ScriptHighlightKind? kind = token.Type switch
            {
                TokenType.COMMENT => ScriptHighlightKind.Comment,
                TokenType.STRING => ScriptHighlightKind.String,
                TokenType.INT or TokenType.Number => ScriptHighlightKind.Number,
                TokenType.VAR or TokenType.CONST => ScriptHighlightKind.Variable,
                TokenType.EX_VAR => ScriptHighlightKind.Label,
                TokenType.IDENT => Commands.Contains(token.Value) ? ScriptHighlightKind.Keyword : ScriptHighlightKind.Function,
                >= TokenType.STRUCT and < TokenType.EOF => ScriptHighlightKind.Keyword,
                TokenType.LogicAnd or TokenType.LogicOr or TokenType.LogicNot => ScriptHighlightKind.Keyword,
                _ => null
            };
            int start = token.Span.Start, end = Math.Min(source.Length, token.Span.End);
            if (kind.HasValue && end > start) spans.Add(new(start, end, kind.Value));
        }
        Spans = spans;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        // Binary search avoids walking the entire document for every visible line.
        int low = 0, high = Spans.Count;
        while (low < high) { int mid = (low + high) / 2; if (Spans[mid].End <= line.Offset) low = mid + 1; else high = mid; }
        for (int i = low; i < Spans.Count && Spans[i].Start < line.EndOffset; i++)
        {
            var span = Spans[i];
            int start = Math.Max(span.Start, line.Offset), end = Math.Min(span.End, line.EndOffset);
            if (start < end)
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(BrushesByKind[(int)span.Kind]));
        }
    }

    private static Brush Color(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
}

internal sealed class ScriptEditorBackground(ScriptEditorBox editor) : IBackgroundRenderer
{
    private static readonly Brush ExecutionFill = new SolidColorBrush(Color.FromArgb(27, 12, 93, 119));
    private static readonly Pen ExecutionBorder = new(new SolidColorBrush(Color.FromArgb(100, 12, 93, 119)), 1);
    private static readonly Pen Guide = new(new SolidColorBrush(Color.FromRgb(199, 214, 223)), 1);
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!editor.ScriptMode || !textView.VisualLinesValid) return;
        double space = textView.WideSpaceWidth;
        foreach (var visual in textView.VisualLines)
        {
            var line = visual.FirstDocumentLine;
            double y = visual.VisualTop - textView.VerticalOffset;
            // Folded bodies are omitted by AvalonEdit; only actual visible lines are drawn.
            string text = editor.Document.GetText(line);
            int columns = 0;
            foreach (char c in text)
            {
                if (c == ' ') columns++;
                else if (c == '\t') columns += EditorSyntax.IndentSize - columns % EditorSyntax.IndentSize;
                else break;
            }
            for (int level = 0; level < columns / EditorSyntax.IndentSize; level++)
            {
                double x = level * EditorSyntax.IndentSize * space - textView.HorizontalOffset + .5;
                if (x >= 0 && x <= textView.ActualWidth)
                    drawingContext.DrawLine(Guide, new Point(x, y), new Point(x, y + visual.Height));
            }
            if (editor.ExecutionLine >= line.LineNumber && editor.ExecutionLine <= visual.LastDocumentLine.LineNumber)
                drawingContext.DrawRectangle(ExecutionFill, ExecutionBorder, new Rect(.5, y + .5, Math.Max(0, textView.ActualWidth - 1), Math.Max(0, visual.Height - 1)));
        }
    }
}

internal sealed class ExecutionMargin(ScriptEditorBox editor) : AbstractMargin
{
    protected override Size MeasureOverride(Size availableSize) => new(14, 0);
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (editor.ExecutionLine < 1 || TextView is null || !TextView.VisualLinesValid) return;
        var visual = TextView.VisualLines.FirstOrDefault(v => v.FirstDocumentLine.LineNumber <= editor.ExecutionLine && v.LastDocumentLine.LineNumber >= editor.ExecutionLine);
        if (visual == null) return;
        double center = visual.VisualTop - TextView.VerticalOffset + TextView.DefaultLineHeight / 2;
        var arrow = new StreamGeometry();
        using (var shape = arrow.Open())
        {
            shape.BeginFigure(new Point(2, center - 4), true, true);
            shape.LineTo(new Point(10, center), true, false);
            shape.LineTo(new Point(2, center + 4), true, false);
        }
        arrow.Freeze();
        drawingContext.DrawGeometry(editor.TryFindResource("AccentBrush") as Brush ?? Brushes.DarkCyan, null, arrow);
    }
}
