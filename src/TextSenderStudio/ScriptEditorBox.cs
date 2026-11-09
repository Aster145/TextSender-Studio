using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TextSenderStudio;

/// <summary>A plain WPF TextBox with editor aids; preserves native text editing and undo.</summary>
public sealed class ScriptEditorBox : TextBox
{
    private readonly ListBox _suggestions;
    private readonly Popup _popup;
    private LineNumbers? _numbers;
    private bool _changing, _scriptMode = true;
    public IReadOnlyList<EditorCompletion> ResourceCompletions { get; set; } = [];
    public Func<string, bool>? NavigateResource { get; set; }
    public event EventHandler? CaretStatusChanged;
    public bool ScriptMode
    {
        get => _scriptMode;
        set { _scriptMode = value; if (!value) CloseCompletion(); }
    }
    public string CaretStatus
    {
        get
        {
            // TextBox line layout can be deferred while its tab is hidden. The status
            // is a document position, so compute it from the source independently.
            int caret = Math.Clamp(CaretIndex, 0, Text.Length), line = 1, lineStart = 0;
            for (int i = 0; i < caret; i++) if (Text[i] == '\n') { line++; lineStart = i + 1; }
            return $"行 {line} · 列 {caret - lineStart + 1}";
        }
    }
    public bool CompletionOpen => _popup.IsOpen;
    public IReadOnlyList<EditorCompletion> VisibleCompletions => _suggestions.Items.Cast<EditorCompletion>().ToArray();

    public ScriptEditorBox()
    {
        SetResourceReference(StyleProperty, typeof(TextBox));
        FontFamily = new("Consolas, Microsoft YaHei UI"); FontSize = 14;
        Padding = new(48, 8, 8, 8); AcceptsReturn = true; AcceptsTab = true; TextWrapping = TextWrapping.NoWrap;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
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
        Loaded += (_, _) =>
        {
            if (_numbers == null && AdornerLayer.GetAdornerLayer(this) is { } layer) { _numbers = new(this); layer.Add(_numbers); }
            InvalidateGutter();
        };
        Unloaded += (_, _) => { CloseCompletion(); if (_numbers != null) AdornerLayer.GetAdornerLayer(this)?.Remove(_numbers); _numbers = null; };
        AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => { CloseCompletion(); InvalidateGutter(); }));
        SizeChanged += (_, _) => InvalidateGutter();
        SelectionChanged += (_, _) => { InvalidateGutter(); CaretStatusChanged?.Invoke(this, EventArgs.Empty); };
        TextChanged += (_, _) =>
        {
            InvalidateGutter();
            if (!_changing && ScriptMode && IsKeyboardFocused)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ShowCompletion(false)));
        };
        LostKeyboardFocus += (_, _) => CloseCompletion();
        ToolTip = "Ctrl+Space 补全 · Tab / Shift+Tab 缩进 · 双击函数跳转 · F12 跳转定义";
    }

    private void InvalidateGutter() => _numbers?.InvalidateVisual();
    public void CloseCompletion() => _popup.IsOpen = false;

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
        int start = CaretIndex == 0 ? 0 : Text.LastIndexOf('\n', CaretIndex - 1) + 1;
        string beforeCaret = Text[start..CaretIndex];
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

    private void ReplaceText(int start, int length, string value)
    {
        _changing = true; BeginChange();
        try { Select(start, length); SelectedText = value; CaretIndex = start + value.Length; SelectionLength = 0; }
        finally { EndChange(); _changing = false; }
    }

    private sealed class LineNumbers(ScriptEditorBox editor) : Adorner(editor)
    {
        protected override void OnRender(DrawingContext drawing)
        {
            IsHitTestVisible = false;
            if (!editor.IsVisible || editor.LineCount == 0) return;
            var muted = editor.TryFindResource("MutedBrush") as Brush ?? Brushes.DimGray;
            var background = editor.TryFindResource("PageBrush") as Brush ?? Brushes.White;
            var line = editor.TryFindResource("LineBrush") as Brush ?? Brushes.LightGray;
            var accent = editor.TryFindResource("AccentBrush") as Brush ?? Brushes.DarkCyan;
            drawing.PushClip(new RectangleGeometry(new Rect(1, 1, editor.Padding.Left - 4, Math.Max(0, editor.ActualHeight - 2))));
            drawing.DrawRectangle(background, null, new Rect(1, 1, editor.Padding.Left - 4, Math.Max(0, editor.ActualHeight - 2)));
            drawing.DrawLine(new Pen(line, 1), new(editor.Padding.Left - 6, 8), new(editor.Padding.Left - 6, Math.Max(8, editor.ActualHeight - 8)));
            int first = Math.Max(0, editor.GetFirstVisibleLineIndex()), last = Math.Max(first, editor.GetLastVisibleLineIndex());
            int selectedLine = editor.GetLineIndexFromCharacterIndex(editor.CaretIndex);
            for (int i = first; i <= last && i < editor.LineCount; i++)
            {
                int index = editor.GetCharacterIndexFromLineIndex(i); if (index < 0) continue;
                var rect = editor.GetRectFromCharacterIndex(index); if (rect.IsEmpty) continue;
                var label = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(editor.FontFamily, FontStyles.Normal, i == selectedLine ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                    editor.FontSize, i == selectedLine ? accent : muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawing.DrawText(label, new Point(editor.Padding.Left - 14 - label.Width, rect.Y));
            }
            drawing.Pop();
        }
    }
}
