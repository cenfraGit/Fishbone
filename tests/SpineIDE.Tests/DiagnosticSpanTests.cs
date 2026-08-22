using AvaloniaEdit.Document;
using Fishbone.Core;
using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

// SourceSpan is 1-based with an exclusive EndColumn; AvaloniaEdit offsets are 0-based. An
// off-by-one here is invisible in review and obvious on screen, so these assert on the exact
// text a squiggle would cover rather than on offsets.
public class DiagnosticSpanTests
{
    private static string Underlined(string text, SourceSpan span)
    {
        var document = new TextDocument(text);
        Assert.True(DiagnosticSpans.TryResolve(document, span, out int start, out int length),
            "expected the span to resolve");
        return document.GetText(start, length);
    }

    private static bool Resolves(string text, SourceSpan span) =>
        DiagnosticSpans.TryResolve(new TextDocument(text), span, out _, out _);

    [Fact]
    public void SingleLineSpan_CoversExactlyTheReportedText()
    {
        // the real span for "Undefined variable 'missing'." in "let x = missing;"
        Assert.Equal("missing", Underlined("let x = missing;", new SourceSpan(1, 9, 1, 16)));
    }

    [Fact]
    public void SpanAtStartOfDocument_IsNotShiftedByOne()
    {
        Assert.Equal("let", Underlined("let x = 1;", new SourceSpan(1, 1, 1, 4)));
    }

    [Fact]
    public void SpanOnALaterLine_UsesThatLine()
    {
        Assert.Equal("missing", Underlined(
            "let a = 1;\nlet b = 2;\nlet c = missing;",
            new SourceSpan(3, 9, 3, 16)));
    }

    [Fact]
    public void UnterminatedStringSpan_StopsBeforeTheNewline()
    {
        // the real lexer span for "Unterminated string literal."
        Assert.Equal("\"oops;", Underlined("let s = \"oops;\n", new SourceSpan(1, 9, 1, 15)));
    }

    [Fact]
    public void LargeLiteralSpan_CoversTheWholeLiteral()
    {
        Assert.Equal("99999999999999999999999999",
            Underlined("let x = 99999999999999999999999999;", new SourceSpan(1, 9, 1, 35)));
    }

    // --------------------------------------------------------------------------------
    // zero-width spans: an end-of-file error points past the last character
    // --------------------------------------------------------------------------------

    [Fact]
    public void ZeroWidthSpan_MarksThePrecedingCharacter()
    {
        // the real span for "Missing ';' at end of file." on "let x = 1"
        Assert.Equal("1", Underlined("let x = 1", new SourceSpan(1, 10, 1, 10)));
    }

    [Fact]
    public void ZeroWidthSpanOnABlankLine_WalksBackOverTheNewline()
    {
        // "Unexpected end of file." lands at [2,1] when line 2 is empty. a fixed-width mark here
        // would float in empty space, so it steps back to the last real character
        Assert.Equal(";", Underlined("let x = 1;\n", new SourceSpan(2, 1, 2, 1)));
    }

    [Fact]
    public void ZeroWidthSpanAtTheVeryStart_MarksTheFirstCharacter()
    {
        Assert.Equal("l", Underlined("let x = 1;", new SourceSpan(1, 1, 1, 1)));
    }

    // --------------------------------------------------------------------------------
    // stale and unknown spans
    // --------------------------------------------------------------------------------

    [Fact]
    public void UnknownSpan_DoesNotResolve()
    {
        // a foreign exception or a plugin failure has no location, so there is nowhere to point
        Assert.False(Resolves("let x = 1;", SourceSpan.None));
    }

    [Fact]
    public void EmptyDocument_DoesNotResolve()
    {
        Assert.False(Resolves("", new SourceSpan(1, 1, 1, 5)));
    }

    [Fact]
    public void SpanPastTheEndOfAShorterDocument_ClampsInsteadOfThrowing()
    {
        // a runtime diagnostic can outlive the text it described
        var document = new TextDocument("let x = 1;");
        Assert.True(DiagnosticSpans.TryResolve(document, new SourceSpan(99, 40, 99, 60),
            out int start, out int length));

        Assert.InRange(start, 0, document.TextLength);
        Assert.InRange(start + length, start, document.TextLength);
    }

    [Fact]
    public void SpanWithColumnPastEndOfLine_ClampsToThatLine()
    {
        var document = new TextDocument("ab\ncdef");
        Assert.True(DiagnosticSpans.TryResolve(document, new SourceSpan(1, 1, 1, 99),
            out int start, out int length));

        Assert.Equal("ab", document.GetText(start, length));
    }

    [Fact]
    public void MultiLineSpan_StopsAtTheEndOfItsFirstLine()
    {
        // a statement-level error (For loop step cannot be zero) reports the whole statement's
        // node; underlining to the end of the first line reads as "this statement is wrong"
        Assert.Equal("for (i in 0, 5, 0) {",
            Underlined("for (i in 0, 5, 0) {\n  print(i);\n}", new SourceSpan(1, 1, 3, 2)));
    }
}