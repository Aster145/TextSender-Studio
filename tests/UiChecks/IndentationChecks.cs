using System.Windows.Controls;
using TextSenderStudio;
using TextSenderStudio.Core.Scripting;

internal static class IndentationChecks
{
    public static void Run(MainWindow main, Action<bool, string> check)
    {
        const string pasted = "# 提醒\r\nFOR\r\n    WAIT 500\r\n    BEEP 800, 200\r\n    NEXT\r\n    ALERT 执行完毕\r\n";
        const string expected = "# 提醒\r\nFOR\r\n    WAIT 500\r\n    BEEP 800, 200\r\nNEXT\r\nALERT 执行完毕\r\n";
        var compiler = new StudioScriptEngine();
        var result = ScriptIndentation.Format(pasted);
        check(result.Text == expected, "save formatter aligns pasted NEXT and following ALERT without changing content");
        check(!compiler.Validate(result.Text).Any(d => d.IsError), "formatted reminder example compiles without executing sound or push");
        check(ScriptIndentation.Format(result.Text).Text == result.Text, "indentation formatting is idempotent");
        check(result.MapPosition(pasted.IndexOf("BEEP", StringComparison.Ordinal) + 2) == expected.IndexOf("BEEP", StringComparison.Ordinal) + 2,
            "formatter maps caret within source token");
        check(result.MapPosition(pasted.IndexOf("ALERT", StringComparison.Ordinal) + 3) == expected.IndexOf("ALERT", StringComparison.Ordinal) + 3,
            "formatter maps positions after removed indentation");
        check(ScriptIndentation.Format("FOR 2\nWAIT 0\nNEXT").MapPosition(6) == 10,
            "caret at an unindented statement follows newly inserted indentation");

        const string nested = "FUNC work()\nIF 1 == 1\nFOR 2\nWAIT 0\nNEXT\nELIF 1 == 2\nPRINT NEXT is text\nELSE\nWHILE 1 == 0\nUNTIL 1 == 1\nWAIT 0\nEND\nEND\nENDIF\nENDFUNC\nCALL work";
        const string nestedExpected = "FUNC work()\n    IF 1 == 1\n        FOR 2\n            WAIT 0\n        NEXT\n    ELIF 1 == 2\n        PRINT NEXT is text\n    ELSE\n        WHILE 1 == 0\n            UNTIL 1 == 1\n                WAIT 0\n            END\n        END\n    ENDIF\nENDFUNC\nCALL work";
        var nestedResult = ScriptIndentation.Format(nested).Text;
        check(nestedResult == nestedExpected && !compiler.Validate(nestedResult).Any(d => d.IsError),
            "formatter matches nested condition, loop and function blocks using EasyCon grammar");
        const string textPayload = "FOR 2\n\tSEND_TEXT \"  FOR # NEXT  \" # keep trailing  \n\tPRINT NEXT\n\t# ENDIF is comment\n\tNEXT\n";
        var payloadResult = ScriptIndentation.Format(textPayload).Text;
        check(payloadResult == "FOR 2\n    SEND_TEXT \"  FOR # NEXT  \" # keep trailing  \n    PRINT NEXT\n    # ENDIF is comment\nNEXT\n",
            "formatter normalizes tabs without rewriting quoted text, PRINT payload or comments");
        string continued = "FOR 2\nSEND_TEXT \"line\\\n  NEXT literal\"\nNEXT\n";
        check(ScriptIndentation.Format(continued).Text == "FOR 2\n    SEND_TEXT \"line\\\n  NEXT literal\"\nNEXT\n",
            "quoted escaped-newline payload keeps continuation whitespace");
        check(ScriptIndentation.Format("IF 1 == 1\r\n\tWAIT 0\n  END\r\n").Text == "IF 1 == 1\r\n    WAIT 0\nEND\r\n",
            "formatter preserves mixed original newline sequences");
        check(ScriptIndentation.Format("NEXT\nWAIT 0").Text == "NEXT\nWAIT 0", "unmatched closing keyword cannot produce negative indentation");

        var nestedBlocks = ScriptIndentation.AnalyzeBlocks(nested);
        check(nestedBlocks.Select(block => (block.StartLine, block.EndLine, block.Depth)).SequenceEqual(
            new[] { (1, 15, 0), (2, 14, 1), (3, 5, 2), (9, 13, 2), (10, 12, 3) }),
            "folding ranges match nested function, condition and loops in source order");
        check(nestedBlocks[0].StartOffset == nested.IndexOf('\n')
            && nestedBlocks[0].EndOffset == nested.IndexOf("ENDFUNC", StringComparison.Ordinal) + "ENDFUNC".Length
            && nestedBlocks[0].Header == "FUNC work()",
            "folding leaves opening header visible and includes closing statement");
        check(ScriptIndentation.AnalyzeBlocks(textPayload).Count == 1
            && ScriptIndentation.AnalyzeBlocks(continued).Single().EndLine == 4,
            "folding ignores block words in comments and ordinary or multiline string payloads");
        check(ScriptIndentation.AnalyzeBlocks("FOR 2\r\nWAIT 0\nNEXT\r\n").Single()
            is { StartOffset: 5, EndOffset: 18, StartLine: 1, EndLine: 3 },
            "folding offsets preserve mixed newline widths");
        check(ScriptIndentation.AnalyzeBlocks("IF 1 == 1\nFOR 2\nENDIF\n").Count == 0
            && ScriptIndentation.AnalyzeBlocks("FOR 2\nNEXT extra\nNEXT").Single().EndLine == 3,
            "folding does not invent matches for incomplete or mismatched blocks");
        check(ScriptIndentation.AnalyzeBlocks("STRUCT item\nEND\nEXTERN FUNC work()\n").Single()
            is { StartLine: 1, EndLine: 2, Header: "STRUCT item" },
            "folding supports generic END and does not open external function declarations");

        var editor = (ScriptEditorBox)main.FindName("ScriptEditor");
        ((TabControl)main.FindName("MainTabs")).SelectedIndex = 0;
        string before = editor.Text;
        try
        {
            editor.ScriptMode = true; editor.Text = pasted;
            editor.Select(pasted.IndexOf("ALERT", StringComparison.Ordinal), "ALERT".Length);
            check(editor.FormatIndentation() && editor.Text == expected && editor.SelectedText == "ALERT",
                "editor formatting retains selected token and applies corrected indentation");
            editor.Undo();
            check(editor.Text == pasted, "save formatting is a single native undo operation");
            int caret = pasted.IndexOf("ALERT", StringComparison.Ordinal) + 2;
            editor.Select(caret, 0); editor.FormatIndentation();
            check(editor.CaretIndex == expected.IndexOf("ALERT", StringComparison.Ordinal) + 2 && editor.SelectionLength == 0,
                "formatting retains caret column within its logical token");
            editor.ScriptMode = false; editor.Text = pasted;
            check(!editor.FormatIndentation() && editor.Text == pasted, "ordinary text save keeps original whitespace unchanged");
        }
        finally { editor.ScriptMode = true; editor.Text = before; editor.Select(0, 0); editor.CloseCompletion(); }
    }
}
