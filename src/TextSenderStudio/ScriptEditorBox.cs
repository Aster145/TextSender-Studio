using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace TextSenderStudio;

/// <summary>A document-backed code editor: folding and markers never change source text.</summary>
public sealed class ScriptEditorBox : TextEditor
{
    private readonly ListBox _suggestions;
    private readonly Popup _popup;
    private readonly ScriptColorizer _colors;
    private readonly ScriptEditorBackground _background;
    private readonly ExecutionMargin _executionMargin;
    private readonly DispatcherTimer _structureTimer;
    private FoldingManager? _folding;
    private bool _changing, _scriptMode = true;
    private int _executionLine;
    private bool _followExecutionLine;
    public int ExecutionLine => _executionLine;
    public IReadOnlyList<EditorCompletion> ResourceCompletions { get; set; } = [];
    public Func<string, bool>? NavigateResource { get; set; }
    public event EventHandler? CaretStatusChanged;
    public int CaretIndex { get => CaretOffset; set => CaretOffset = value; }
    public bool ScriptMode
    {
        get => _scriptMode;
        set
        {
            if (_scriptMode == value) return;
            _scriptMode = value;
            if (!value) { CloseCompletion(); ClearExecutionLine(); }
            RefreshStructure();
        }
    }
    public string CaretStatus => $"行 {TextArea.Caret.Line} · 列 {TextArea.Caret.Column}";
    public bool CompletionOpen => _popup.IsOpen;
    public IReadOnlyList<EditorCompletion> VisibleCompletions => _suggestions.Items.Cast<EditorCompletion>().ToArray();
    public IReadOnlyList<FoldingSection> Foldings => _folding?.AllFoldings.ToArray() ?? [];
    public IReadOnlyList<ScriptHighlightSpan> HighlightSpans => _colors.Spans;

    public ScriptEditorBox()
    {
        FontFamily = new("Consolas, Microsoft YaHei UI"); FontSize = 14;
        Padding = new(8); ShowLineNumbers = true;
        SetResourceReference(BackgroundProperty, "PanelBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        SetResourceReference(BorderBrushProperty, "LineBrush"); BorderThickness = new(1);
        SetResourceReference(LineNumbersForegroundProperty, "MutedBrush");
        Options.IndentationSize = EditorSyntax.IndentSize; Options.ConvertTabsToSpaces = true;
        Options.EnableHyperlinks = false; Options.EnableEmailHyperlinks = false;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _colors = new ScriptColorizer();
        TextArea.TextView.LineTransformers.Add(_colors);
        _background = new ScriptEditorBackground(this);
        TextArea.TextView.BackgroundRenderers.Add(_background);
        _executionMargin = new ExecutionMargin(this);
        TextArea.LeftMargins.Insert(0, _executionMargin);
        _structureTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _structureTimer.Tick += (_, _) => RefreshStructure();
        _suggestions = new ListBox { MaxHeight = 240, MinWidth = 320, MaxWidth = 560, Focusable = false, FontSize = 14 };
        _suggestions.SetResourceReference(BackgroundProperty, "PanelBrush");
        _suggestions.SetResourceReference(ForegroundProperty, "TextBrush");
        _suggestions.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_suggestions, e.OriginalSource as DependencyObject) is ListBoxItem item)
            { _suggestions.SelectedItem = item.Content; AcceptCompletion(); e.Handled = true; }
        };
        var chrome = new Border { Child = _suggestions, BorderThickness = new(1), CornerRadius = new(8), Padding = new(4) };
        chrome.SetResourceReference(BorderBrushProperty, "LineBrush"); chrome.SetResourceReference(BackgroundProperty, "PanelBrush");
        _popup = new Popup { Child = chrome, PlacementTarget = this, Placement = PlacementMode.Relative, StaysOpen = false, AllowsTransparency = true, Focusable = false };
        Loaded += (_, _) => { RefreshStructure(); if (_executionLine > 0 && _followExecutionLine) FollowExecutionLine(); };
        Unloaded += (_, _) => { _structureTimer.Stop(); CloseCompletion(); };
        TextArea.TextView.ScrollOffsetChanged += (_, _) => { CloseCompletion(); _executionMargin.InvalidateVisual(); };
        TextArea.TextView.VisualLinesChanged += (_, _) => _executionMargin.InvalidateVisual();
        TextArea.Caret.PositionChanged += (_, _) => CaretStatusChanged?.Invoke(this, EventArgs.Empty);
        TextChanged += (_, _) =>
        {
            ClearExecutionLine();
            _structureTimer.Stop(); _structureTimer.Start();
            if (!_changing && ScriptMode && IsKeyboardFocusWithin)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ShowCompletion(false)));
        };
        LostKeyboardFocus += (_, _) => CloseCompletion();
        ToolTip = "Ctrl+S 格式化并保存 · Ctrl+Space 补全 · Tab / Shift+Tab 缩进 · 双击函数 / F12 跳转 · 点击行号旁的标记折叠";
    }

    public void RefreshStructure()
    {
        _structureTimer.Stop();
        _colors.Update(ScriptMode ? Text : "");
        if (ScriptMode)
        {
            _folding ??= FoldingManager.Install(TextArea);
            _folding.UpdateFoldings(ScriptIndentation.AnalyzeBlocks(Text).Select(b => new NewFolding(b.StartOffset, b.EndOffset)
            { Name = " … " }), -1);
        }
        else if (_folding != null) { FoldingManager.Uninstall(_folding); _folding = null; }
        TextArea.TextView.Redraw();
        InvalidateGutter();
    }

    // Compatibility methods use zero-based line indices, matching the previous editor.
    public int GetCharacterIndexFromLineIndex(int line) => line >= 0 && line < LineCount ? Document.GetLineByNumber(line + 1).Offset : -1;
    public int GetLineIndexFromCharacterIndex(int offset) => Document.GetLineByOffset(Math.Clamp(offset, 0, Document.TextLength)).LineNumber - 1;
    public int GetFirstVisibleLineIndex()
    {
        TextArea.TextView.EnsureVisualLines();
        return TextArea.TextView.VisualLines.FirstOrDefault()?.FirstDocumentLine.LineNumber - 1 ?? 0;
    }
    public int GetLastVisibleLineIndex()
    {
        TextArea.TextView.EnsureVisualLines();
        return TextArea.TextView.VisualLines.LastOrDefault()?.LastDocumentLine.LineNumber - 1 ?? 0;
    }
    public new void ScrollToLine(int line) => base.ScrollToLine(Math.Clamp(line + 1, 1, LineCount));
    public Rect GetRectFromCharacterIndex(int offset)
    {
        var view = TextArea.TextView;
        var position = new TextViewPosition(Document.GetLocation(Math.Clamp(offset, 0, Document.TextLength)));
        var point = view.GetVisualPosition(position, VisualYPosition.LineTop) - view.ScrollOffset;
        point = view.TranslatePoint(point, this);
        return new Rect(point, new Size(1, view.DefaultLineHeight));
    }
    public int GetCharacterIndexFromPoint(Point point, bool snapToText)
    {
        var position = GetPositionFromPoint(point);
        return position.HasValue ? Document.GetOffset(position.Value.Location) : -1;
    }
    private void InvalidateGutter()
    { TextArea.TextView.InvalidateLayer(KnownLayer.Background); _executionMargin.InvalidateVisual(); }
    public void CloseCompletion() => _popup.IsOpen = false;
    public void ShowExecutionLine(int line, bool follow = true)
    {
        if (!ScriptMode || line < 1 || line > LineCount) { ClearExecutionLine(); return; }
        if (_executionLine == line) return;
        _executionLine = line; _followExecutionLine = follow;
        ExpandLine(line);
        if (follow && IsVisible) FollowExecutionLine();
        InvalidateGutter();
    }
    public void ClearExecutionLine()
    {
        if (_executionLine == 0) return;
        _executionLine = 0; _followExecutionLine = false; InvalidateGutter();
    }
    private void ExpandLine(int line)
    {
        int offset = Document.GetLineByNumber(line).Offset;
        if (_folding != null)
            foreach (var fold in _folding.GetFoldingsContaining(offset)) fold.IsFolded = false;
    }
    private void FollowExecutionLine()
    {
        int target = _executionLine - 1;
        if (target < 0 || target >= LineCount) return;
        int first = GetFirstVisibleLineIndex(), last = GetLastVisibleLineIndex();
        if (target < first || target > last) ScrollToLine(target);
    }

    public void ShowCompletion(bool explicitRequest = true)
    {
        if (!ScriptMode || IsReadOnly) { CloseCompletion(); return; }
        int caret = CaretIndex;
        if (!explicitRequest && (caret == 0 || !EditorSyntax.IsTokenCharacter(Text[caret - 1]) || SelectionLength != 0)) { CloseCompletion(); return; }
        var matches = EditorSyntax.Complete(Text, caret, ResourceCompletions);
        if (matches.Count == 0) { CloseCompletion(); return; }
        _suggestions.ItemsSource = matches; _suggestions.SelectedIndex = 0;
        var rect = GetRectFromCharacterIndex(caret);
        _popup.HorizontalOffset = Math.Clamp(rect.IsEmpty ? Padding.Left : rect.X, 0, Math.Max(0, ActualWidth - 320));
        _popup.VerticalOffset = rect.IsEmpty ? 32 : rect.Bottom + 4;
        _popup.IsOpen = true;
    }

    public void AcceptCompletion()
    {
        if (_suggestions.SelectedItem is not EditorCompletion item) return;
        var (start, end) = EditorSyntax.TokenAt(Text, CaretIndex);
        ReplaceText(start, end - start, item.Text); CloseCompletion(); Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (ScriptMode && !IsReadOnly)
        {
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control) { ShowCompletion(); e.Handled = true; return; }
            if (e.Key == Key.F12) { NavigateAt(CaretIndex); e.Handled = true; return; }
            if (_popup.IsOpen)
            {
                if (e.Key is Key.Up or Key.Down)
                {
                    _suggestions.SelectedIndex = Math.Clamp(_suggestions.SelectedIndex + (e.Key == Key.Up ? -1 : 1), 0, _suggestions.Items.Count - 1);
                    _suggestions.ScrollIntoView(_suggestions.SelectedItem); e.Handled = true; return;
                }
                if (e.Key == Key.Escape) { CloseCompletion(); e.Handled = true; return; }
                if ((e.Key is Key.Enter or Key.Tab) && Keyboard.Modifiers == ModifierKeys.None) { AcceptCompletion(); e.Handled = true; return; }
                if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) CloseCompletion();
            }
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { InsertIndentedNewLine(); e.Handled = true; return; }
            if (e.Key == Key.Tab && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            { IndentSelection(Keyboard.Modifiers == ModifierKeys.Shift); e.Handled = true; return; }
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseDoubleClick(MouseButtonEventArgs e)
    {
        if (ScriptMode && e.ChangedButton == MouseButton.Left)
        {
            int index = GetCharacterIndexFromPoint(e.GetPosition(this), true);
            if (index >= 0 && NavigateAt(index)) { e.Handled = true; return; }
        }
        base.OnPreviewMouseDoubleClick(e);
    }

    public bool NavigateAt(int position)
    {
        if (!ScriptMode || EditorSyntax.InCommentOrString(Text, Math.Clamp(position, 0, Text.Length))) return false;
        var (start, end) = EditorSyntax.TokenAt(Text, position); if (start == end) return false;
        string name = Text[start..end]; CloseCompletion();
        var definition = EditorSyntax.FindDefinitions(Text).FirstOrDefault(d => d.Name == name);
        if (definition.Name != null)
        {
            int line = 0;
            for (int i = 0; i < definition.Position; i++) if (Text[i] == '\n') line++;
            Select(definition.Position, name.Length); ScrollToLine(line); Focus(); return true;
        }
        return NavigateResource?.Invoke(name) == true;
    }

    public void InsertIndentedNewLine()
    {
        // Enter replaces the selection, regardless of which end contains the caret.
        int insertion = SelectionStart;
        int start = insertion == 0 ? 0 : Text.LastIndexOf('\n', insertion - 1) + 1;
        string beforeCaret = Text[start..insertion];
        string indent = EditorSyntax.LeadingWhitespace(beforeCaret);
        string corrected = indent;
        // Find the surrounding block's level for a closing line, instead of repeatedly
        // removing indentation from an already-aligned ELSE / ENDIF.
        if (EditorSyntax.ClosesBlock(beforeCaret)) corrected = ExpectedClosingIndent(start, beforeCaret);
        string newIndent = corrected + (EditorSyntax.OpensBlock(beforeCaret) ? new string(' ', EditorSyntax.IndentSize) : "");
        int selectionEnd = SelectionStart + SelectionLength;
        string current = corrected + beforeCaret[indent.Length..];
        ReplaceText(start, selectionEnd - start, current + Environment.NewLine + newIndent);
        CloseCompletion();
    }

    private string ExpectedClosingIndent(int lineStart, string line)
    {
        var blocks = new Stack<string>();
        foreach (string previous in Text[..lineStart].Split('\n'))
        {
            string word = previous.TrimStart().Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() ?? "";
            if (word is "IF" or "FOR" or "WHILE" or "UNTIL" or "FUNC") blocks.Push(EditorSyntax.LeadingWhitespace(previous));
            else if (word is "ENDIF" or "ENDFUNC" or "NEXT" or "END") { if (blocks.Count > 0) blocks.Pop(); }
        }
        return blocks.TryPeek(out var indent) ? indent : EditorSyntax.LeadingWhitespace(line);
    }

    public void IndentSelection(bool unindent)
    {
        CloseCompletion();
        if (SelectionLength == 0 && !unindent)
        { ReplaceText(CaretIndex, 0, new string(' ', EditorSyntax.IndentSize)); return; }
        int selectedStart = SelectionStart, selectedEnd = SelectionStart + SelectionLength;
        int start = selectedStart == 0 ? 0 : Text.LastIndexOf('\n', selectedStart - 1) + 1;
        int end = selectedEnd;
        if (end > start && end <= Text.Length && Text[end - 1] == '\n') end--;
        int lineEnd = Text.IndexOf('\n', end); if (lineEnd < 0) lineEnd = Text.Length;
        string original = Text[start..lineEnd];
        string changed = string.Join("\n", original.Split('\n').Select(line =>
        {
            if (!unindent) return new string(' ', EditorSyntax.IndentSize) + line;
            var indent = EditorSyntax.LeadingWhitespace(line); return EditorSyntax.RemoveIndent(indent) + line[indent.Length..];
        }));
        bool hadSelection = SelectionLength > 0;
        ReplaceText(start, lineEnd - start, changed);
        if (hadSelection) Select(start, changed.Length);
        else CaretIndex = Math.Max(start, selectedStart + changed.Length - original.Length);
    }

    public void InsertSnippet(string text)
    {
        if (!ScriptMode || IsReadOnly) return;
        string newline = Environment.NewLine;
        ReplaceText(SelectionStart, SelectionLength, text.Replace("\r\n", "\n").Replace("\n", newline)); Focus(); CloseCompletion();
    }

    public bool FormatIndentation()
    {
        if (!ScriptMode || IsReadOnly) return false;
        var formatted = ScriptIndentation.Format(Text);
        if (formatted.Text == Text) return false;
        int start = formatted.MapPosition(SelectionStart);
        int end = formatted.MapPosition(SelectionStart + SelectionLength);
        double vertical = VerticalOffset, horizontal = HorizontalOffset;
        bool wasChanging = _changing;
        _changing = true; Document.BeginUpdate();
        try
        {
            Select(0, Text.Length); SelectedText = formatted.Text;
            Select(start, end - start);
        }
        finally { Document.EndUpdate(); _changing = wasChanging; }
        ScrollToVerticalOffset(vertical); ScrollToHorizontalOffset(horizontal);
        CloseCompletion();
        return true;
    }

    private void ReplaceText(int start, int length, string value)
    {
        _changing = true; Document.BeginUpdate();
        try { Select(start, length); SelectedText = value; CaretIndex = start + value.Length; SelectionLength = 0; }
        finally { Document.EndUpdate(); _changing = false; }
    }

}
