using Fishbone.Core;

namespace Fishbone.Engine.Tests;

// covers the one path a client uses to report anything that went wrong, and the spans
// that reach it. the point of FishboneDiagnostics.From is that a client never tests for
// an exception type itself, so these tests go through it rather than around it.
public class DiagnosticTests
{
    private static IReadOnlyList<FishboneDiagnostic> Run(string code)
    {
        var exception = Record.Exception(() => FishboneEngine.Run(code, new FishboneConfiguration()));
        Assert.NotNull(exception);
        return FishboneDiagnostics.From(exception);
    }

    // --------------------------------------------------------------------------------
    // classification
    // --------------------------------------------------------------------------------

    [Fact]
    public void SyntaxError_ClassifiesAsParseWithoutTheCallerCheckingTheType()
    {
        var diagnostic = Assert.Single(Run("let x = 1"));

        Assert.Equal(DiagnosticStage.Parse, diagnostic.Stage);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void RuntimeError_ClassifiesAsRuntime()
    {
        var diagnostic = Assert.Single(Run("let x = nope;"));

        Assert.Equal(DiagnosticStage.Runtime, diagnostic.Stage);
        Assert.Contains("Undefined variable 'nope'", diagnostic.Message);
    }

    [Fact]
    public void SeveralSyntaxErrors_AllComeBackFromOneCall()
    {
        // the case that forced clients to know FishboneParseException holds a list while
        // FishboneRuntimeException is a single
        var diagnostics = Run("let y = 1 @ 2;\nif x { }\n");

        Assert.True(diagnostics.Count > 1);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void ForeignException_BecomesASingleRuntimeDiagnostic()
    {
        // a .NET exception escaping a host call is not a Fishbone exception at all, and
        // used to fall through every branch to a bare Message
        var config = new FishboneConfiguration();
        config.AddBuiltIn("boom", new Action(() => throw new InvalidOperationException("host blew up")));

        var exception = Record.Exception(() => FishboneEngine.Run("boom();", config));

        var diagnostic = Assert.Single(FishboneDiagnostics.From(exception!));
        Assert.Equal(DiagnosticStage.Runtime, diagnostic.Stage);
        Assert.Contains("host blew up", diagnostic.Message);
    }

    [Fact]
    public void UnrelatedException_IsStillReportable()
    {
        var diagnostic = Assert.Single(FishboneDiagnostics.From(new InvalidOperationException("plain")));

        Assert.Equal(DiagnosticStage.Runtime, diagnostic.Stage);
        Assert.Equal("plain", diagnostic.Message);
        Assert.False(diagnostic.Span.IsKnown);
    }

    [Fact]
    public void SingleInnerAggregate_IsUnwrapped()
    {
        // a Task boundary wraps the real exception, and unwrapping keeps the location
        var inner = new FishboneRuntimeException("inner failure", new SourceSpan(3, 5, 3, 9));

        var diagnostic = Assert.Single(FishboneDiagnostics.From(new AggregateException(inner)));

        Assert.Equal("inner failure", diagnostic.Message);
        Assert.Equal(new SourceSpan(3, 5, 3, 9), diagnostic.Span);
    }

    [Fact]
    public void From_RejectsNull() =>
        Assert.Throws<ArgumentNullException>(() => FishboneDiagnostics.From(null!));

    // --------------------------------------------------------------------------------
    // spans
    // --------------------------------------------------------------------------------

    [Fact]
    public void RuntimeError_CarriesASizedSpanNotJustACaret()
    {
        // the failing node is the identifier, so the span should cover exactly its text.
        // this is what lets a client underline instead of highlighting a whole line
        var diagnostic = Assert.Single(Run("let x = missing;"));

        Assert.Equal(1, diagnostic.Span.Line);
        Assert.Equal(9, diagnostic.Span.Column);
        Assert.Equal(16, diagnostic.Span.EndColumn);   // one past 'missing'
        Assert.True(diagnostic.Span.IsSingleLine);
    }

    [Fact]
    public void RuntimeError_ReportsTheInnermostNodeNotTheStatement()
    {
        // the error is on line 3, not on line 1 where the enclosing block starts
        var diagnostic = Assert.Single(Run("""
            let a = 1;
            let b = 2;
            let c = missing;
            """));

        Assert.Equal(3, diagnostic.Span.Line);
    }

    [Fact]
    public void SpanNone_ReadsAsUnknown()
    {
        Assert.False(SourceSpan.None.IsKnown);
        Assert.False(SourceSpan.None.IsSingleLine);
        Assert.Equal("unknown location", SourceSpan.None.ToString());
    }

    [Fact]
    public void ZeroWidthSpan_IsKnownButNotSingleLine()
    {
        // a start-only position: a client can place a caret but must not try to size an underline
        var span = new SourceSpan(4, 7);

        Assert.True(span.IsKnown);
        Assert.False(span.IsSingleLine);
        Assert.Equal(4, span.EndLine);
        Assert.Equal(7, span.EndColumn);
    }

    // --------------------------------------------------------------------------------
    // plugin loading
    // --------------------------------------------------------------------------------

    [Fact]
    public void PluginLoad_ReportsFailuresAsDiagnosticsInsteadOfPrinting()
    {
        // a gui host has no console, so the loader must hand its failures back.
        // a dll that is not a managed assembly is the cheapest way to force one
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(directory, "broken"));
        File.WriteAllText(Path.Combine(directory, "broken", "notreal.dll"), "this is not an assembly");

        try
        {
            var result = FishbonePluginLoader.Load(directory, new FishboneConfiguration());

            Assert.Empty(result.Loaded);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(DiagnosticStage.Configuration, diagnostic.Stage);
            Assert.Contains("Failed to load plugin assembly", diagnostic.Message);
            Assert.False(diagnostic.Span.IsKnown);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PluginLoad_MissingDirectoryReportsNothing()
    {
        var result = FishbonePluginLoader.Load(
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), new FishboneConfiguration());

        Assert.Empty(result.Loaded);
        Assert.Empty(result.Diagnostics);
    }
}