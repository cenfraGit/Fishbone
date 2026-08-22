using Fishbone.Core;

namespace Fishbone.Parser.Tests;

// covers the rewriting of antlr's own error text, and the spans that go with it.
// the messages asserted here are what a script author actually reads, so the point
// of these tests is that antlr's internal vocabulary (token names, "mismatched
// input", "token recognition error") never reaches them.
public class ParseErrorMessageTests
{
    private static IReadOnlyList<FishboneDiagnostic> DiagnosticsFor(string code)
    {
        var exception = Assert.Throws<FishboneParseException>(() => ASTParser.Parse(code));
        return exception.Diagnostics;
    }

    private static FishboneDiagnostic SingleDiagnosticFor(string code) =>
        Assert.Single(DiagnosticsFor(code));

    [Fact]
    public void MissingSemicolonAtEndOfInput_NamesTheEndOfFile()
    {
        var diagnostic = SingleDiagnosticFor("let x = 1");

        Assert.Equal("Missing ';' at end of file.", diagnostic.Message);
        Assert.Equal(DiagnosticStage.Parse, diagnostic.Stage);
    }

    [Fact]
    public void MissingTokenBeforeAnother_NamesBoth()
    {
        // 'if' requires parentheses, so this reports the missing '(' against the condition
        var diagnostics = DiagnosticsFor("if x { }");

        Assert.Contains(diagnostics, d => d.Message == "Missing '(' before 'x'.");
    }

    [Fact]
    public void UnterminatedString_IsNamedOutright()
    {
        // antlr only reports an unrecognised character run starting at the quote, which
        // explains nothing. the lexer stage is what produced it
        var diagnostics = DiagnosticsFor("let s = \"oops;\n");
        var lexical = Assert.Single(diagnostics, d => d.Stage == DiagnosticStage.Lex);

        Assert.Equal("Unterminated string literal.", lexical.Message);
    }

    [Fact]
    public void UnterminatedString_SpanCoversTheStringAndNotTheNewline()
    {
        var lexical = Assert.Single(DiagnosticsFor("let s = \"oops;\n"), d => d.Stage == DiagnosticStage.Lex);

        Assert.Equal(1, lexical.Span.Line);
        Assert.Equal(9, lexical.Span.Column);       // the opening quote
        Assert.Equal(15, lexical.Span.EndColumn);   // one past ';', so the newline is excluded
        Assert.True(lexical.Span.IsSingleLine);
    }

    [Fact]
    public void UnexpectedCharacter_IsReportedAsOne()
    {
        var diagnostics = DiagnosticsFor("let y = 1 @ 2;");
        var lexical = Assert.Single(diagnostics, d => d.Stage == DiagnosticStage.Lex);

        Assert.Equal("Unexpected character '@'.", lexical.Message);
    }

    [Fact]
    public void LargeExpectingSet_IsNotDumpedAtTheUser()
    {
        // this is the case that used to print
        //   mismatched input '<EOF>' expecting {'{', '(', '[', INT, DOUBLE, STRING, ... }
        var diagnostics = DiagnosticsFor("let x = ;");

        Assert.All(diagnostics, diagnostic =>
        {
            Assert.DoesNotContain("expecting", diagnostic.Message);
            Assert.DoesNotContain("INTERP_STRING", diagnostic.Message);
            Assert.DoesNotContain("mismatched input", diagnostic.Message);
        });
    }

    [Fact]
    public void SingleExpectation_IsKeptBecauseItIsUseful()
    {
        // an expecting set of one is the most actionable thing the parser can say,
        // so unlike a large set it survives the rewrite
        var diagnostics = DiagnosticsFor("let y = 1 @ 2;");

        Assert.Contains(diagnostics, d => d.Message == "Unexpected '2', expected ';'.");
    }

    [Fact]
    public void NoMessageLeaksAntlrVocabulary()
    {
        string[] sources =
        [
            "let x = 1",
            "let s = \"oops;\n",
            "let y = 1 @ 2;",
            "if x { }",
            "let x = ;",
            "func f( { }",
            "let d = {1: };",
        ];

        foreach (var source in sources)
            foreach (var diagnostic in DiagnosticsFor(source))
            {
                Assert.DoesNotContain("token recognition error", diagnostic.Message);
                Assert.DoesNotContain("mismatched input", diagnostic.Message);
                Assert.DoesNotContain("extraneous input", diagnostic.Message);
                Assert.DoesNotContain("no viable alternative", diagnostic.Message);
                // a literal escape would mean an error text carried a newline through
                Assert.DoesNotContain("\\n", diagnostic.Message);
            }
    }

    [Fact]
    public void RawMessage_IsKeptWheneverTheMessageWasRewritten()
    {
        var diagnostic = SingleDiagnosticFor("let x = 1");

        Assert.NotNull(diagnostic.RawMessage);
        Assert.Contains("missing ';'", diagnostic.RawMessage);
        Assert.NotEqual(diagnostic.Message, diagnostic.RawMessage);
    }

    [Fact]
    public void OffendingText_IsCarriedForTheParserStage()
    {
        var diagnostics = DiagnosticsFor("if x { }");

        Assert.Contains(diagnostics, d => d.OffendingText == "x");
    }

    [Fact]
    public void SeveralSyntaxErrors_AreAllReported()
    {
        var diagnostics = DiagnosticsFor("let y = 1 @ 2;\nif x { }\n");

        Assert.True(diagnostics.Count > 1, $"expected several diagnostics, got {diagnostics.Count}");
        Assert.Contains(diagnostics, d => d.Stage == DiagnosticStage.Lex);
        Assert.Contains(diagnostics, d => d.Stage == DiagnosticStage.Parse);
    }

    [Fact]
    public void LegacyErrorsView_StillProjectsTheDiagnostics()
    {
        // ParseError is public api, so the older shape has to keep working
        var exception = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = 1"));

        var error = Assert.Single(exception.Errors);
        Assert.Equal(exception.Diagnostics[0].Message, error.Message);
        Assert.Equal(exception.Diagnostics[0].Span, error.Span);
        Assert.Equal(1, error.Line);   // no trailing newline, so EOF is still on line 1
    }

    [Fact]
    public void AstBuilderError_CarriesAFullSpanNotJustAPoint()
    {
        // a literal too large for long is rejected by the ast builder rather than by antlr,
        // and those throw sites now pass the context's whole extent
        var diagnostic = SingleDiagnosticFor("let x = 99999999999999999999999999;");

        Assert.Contains("too large", diagnostic.Message);
        Assert.True(diagnostic.Span.IsSingleLine, "expected a sized span, not a caret");
        Assert.Equal(9, diagnostic.Span.Column);
        Assert.Equal(35, diagnostic.Span.EndColumn);
    }
}