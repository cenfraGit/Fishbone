using Fishbone.Core;

namespace Fishbone.Parser.Tests;

// TryParse is the non-throwing form of Parse, meant for a caller that expects invalid input to
// be normal (an editor validating as the user types). These tests pin that it reports exactly
// what Parse would have thrown, since the two must not drift.
public class TryParseTests
{
    [Fact]
    public void ValidScript_ReturnsTrueWithNoDiagnostics()
    {
        Assert.True(ASTParser.TryParse("let x = 1;", out var ast, out var diagnostics));

        Assert.NotNull(ast);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("let x = 1")]                                  // grammar: missing ';'
    [InlineData("let s = \"oops;\n")]                           // lexer: unterminated string
    [InlineData("let y = 1 @ 2;")]                              // lexer: bad character
    [InlineData("let x = 99999999999999999999999999;")]         // visitor: literal too large
    [InlineData("let s = \"\\q\";")]                            // visitor: bad escape
    [InlineData("try { let x = 1; }")]                          // visitor: try with no catch/finally
    public void InvalidScript_ReportsInsteadOfThrowing(string code)
    {
        Assert.True(!ASTParser.TryParse(code, out var ast, out var diagnostics));

        Assert.Null(ast);
        Assert.NotEmpty(diagnostics);
    }

    [Theory]
    [InlineData("let x = 1")]
    [InlineData("let s = \"oops;\n")]
    [InlineData("let x = 99999999999999999999999999;")]
    [InlineData("let s = \"\\q\";")]
    [InlineData("try { let x = 1; }")]
    public void TryParse_ReportsTheSameDiagnosticsParseThrows(string code)
    {
        // the visitor diagnoses some of these by throwing, so this is the case most likely to
        // drift if TryParse ever grows its own error handling
        var thrown = Assert.Throws<FishboneParseException>(() => ASTParser.Parse(code));
        ASTParser.TryParse(code, out _, out var reported);

        Assert.Equal(
            thrown.Diagnostics.Select(d => (d.Stage, d.Message, d.Span)),
            reported.Select(d => (d.Stage, d.Message, d.Span)));
    }

    [Fact]
    public void VisitorDiagnostics_KeepTheirSpan()
    {
        // a visitor-thrown diagnostic must survive TryParse with its location intact, since it is
        // reached through a catch rather than through the error listener
        ASTParser.TryParse("let x = 99999999999999999999999999;", out _, out var diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("too large", diagnostic.Message);
        Assert.True(diagnostic.Span.IsSingleLine);
        Assert.Equal(9, diagnostic.Span.Column);
    }

    [Fact]
    public void SeveralSyntaxErrors_AreAllReported()
    {
        ASTParser.TryParse("let y = 1 @ 2;\nif x { }\n", out _, out var diagnostics);

        Assert.True(diagnostics.Count > 1, $"expected several, got {diagnostics.Count}");
    }

    [Fact]
    public void EmptySource_IsValid()
    {
        Assert.True(ASTParser.TryParse("", out var ast, out var diagnostics));

        Assert.NotNull(ast);
        Assert.Empty(diagnostics);
    }
}