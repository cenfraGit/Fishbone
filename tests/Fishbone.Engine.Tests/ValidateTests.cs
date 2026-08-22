using Fishbone.Core;

namespace Fishbone.Engine.Tests;

// Validate is the engine's parse-only surface, for a caller that checks a script without running
// it. It must never throw for a bad script, since an editor validating as the user types would
// otherwise raise on nearly every keystroke.
public class ValidateTests
{
    [Fact]
    public void ValidScript_ReturnsEmpty()
    {
        Assert.Empty(FishboneEngine.Validate("let x = 1;\nprintln(x);"));
    }

    [Fact]
    public void EmptyScript_ReturnsEmpty()
    {
        Assert.Empty(FishboneEngine.Validate(""));
    }

    [Theory]
    [InlineData("let x = 1")]
    [InlineData("let s = \"oops;\n")]
    [InlineData("let y = 1 @ 2;")]
    [InlineData("let x = 99999999999999999999999999;")]
    [InlineData("try { let x = 1; }")]
    public void InvalidScript_ReportsWithoutThrowing(string code)
    {
        var diagnostics = FishboneEngine.Validate(code);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void Diagnostics_CarryASpanTheEditorCanUnderline()
    {
        var diagnostic = Assert.Single(FishboneEngine.Validate("let x = 99999999999999999999999999;"));

        Assert.True(diagnostic.Span.IsSingleLine);
        Assert.Equal(9, diagnostic.Span.Column);
        Assert.Equal(35, diagnostic.Span.EndColumn);
    }

    [Fact]
    public void Validate_DoesNotExecuteTheScript()
    {
        // a runtime error means nothing here: validation stops at parsing, so a script that would
        // fail at run time still validates clean
        Assert.Empty(FishboneEngine.Validate("let x = definitelyNotDefined;"));
    }

    [Fact]
    public void Validate_ReportsWhatRunningWouldHaveThrownForSyntax()
    {
        const string code = "let x = 1";

        var fromValidate = FishboneEngine.Validate(code);
        var fromRun = Record.Exception(() => FishboneEngine.Run(code, new FishboneConfiguration()));

        Assert.Equal(
            FishboneDiagnostics.From(fromRun!).Select(d => (d.Stage, d.Message, d.Span)),
            fromValidate.Select(d => (d.Stage, d.Message, d.Span)));
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => FishboneEngine.Validate(null!));
}