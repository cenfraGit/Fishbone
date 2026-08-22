namespace Fishbone.Parser.Tests;

// the guard exists to stop a stack overflow, which cannot be caught and takes the process down.
// what it defends is evaluation, not parsing: parsing runs on an enlarged stack and copes with
// about 2500 chained operators, while evaluating the resulting tree runs on the caller's stack
// and dies between 400 and 500 in a debug build, around 900 in release. these tests pin that the
// guard refuses well before that, and does not refuse real code.
public class ParseDepthGuardTests
{
    private static string Chain(int terms) => "let x = " + string.Join(" + ", Enumerable.Repeat("1", terms)) + ";";
    private static string Nested(char open, char close, int depth) =>
        "let x = " + new string(open, depth) + "1" + new string(close, depth) + ";";

    [Fact]
    public void EmptySource_IsFine() => Assert.False(ParseDepthGuard.LooksTooDeepToParse(""));

    [Theory]
    [InlineData("let x = 1;")]
    [InlineData("let x = (1 + 2) * (3 - 4) / 5;")]
    [InlineData("func f(a, b) { return a + b * 2 - 1; }")]
    [InlineData("let m = [[1, 2], [3, 4]];")]
    [InlineData("if (a > 1 && b < 2 || c == 3) { print(a); }")]
    public void OrdinaryCode_IsNotRefused(string code) =>
        Assert.False(ParseDepthGuard.LooksTooDeepToParse(code));

    [Fact]
    public void EverySampleScript_IsNotRefused()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Fishbone.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        foreach (string file in Directory.GetFiles(Path.Combine(dir!.FullName, "samples"), "*.fb"))
            Assert.False(ParseDepthGuard.LooksTooDeepToParse(File.ReadAllText(file)),
                $"{Path.GetFileName(file)} should not be refused");
    }

    // --------------------------------------------------------------------------------
    // the shapes that actually overflow
    // --------------------------------------------------------------------------------

    [Fact]
    public void A500TermBinaryChain_IsRefused() =>
        // measured to overflow on a default stack, from only about 2KB of text
        Assert.True(ParseDepthGuard.LooksTooDeepToParse(Chain(500)));

    [Theory]
    [InlineData('(', ')')]
    [InlineData('[', ']')]
    public void DeepNesting_IsRefused(char open, char close) =>
        Assert.True(ParseDepthGuard.LooksTooDeepToParse(Nested(open, close, 500)));

    [Fact]
    public void DeeplyNestedBlocks_AreRefused()
    {
        string code = string.Concat(Enumerable.Repeat("if (true) {\n", 500))
            + "let x = 1;\n"
            + string.Concat(Enumerable.Repeat("}\n", 500));

        Assert.True(ParseDepthGuard.LooksTooDeepToParse(code));
    }

    [Fact]
    public void TheGuardRefusesWellBeforeTheInterpreterBreaks()
    {
        // a chain just under the limit still parses, so the guard is not merely refusing
        // everything: it has to let through what is safe
        string safe = Chain(ParseDepthGuard.DepthLimit - 10);

        Assert.False(ParseDepthGuard.LooksTooDeepToParse(safe));
        Assert.True(ASTParser.TryParse(safe, out _, out _), "input under the limit should parse");
    }

    [Fact]
    public void OverTheLimit_TryParseReportsInsteadOfThrowing()
    {
        Assert.False(ASTParser.TryParse(Chain(500), out var ast, out var diagnostics));

        Assert.Null(ast);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("nested too deeply", diagnostic.Message);
        // the check counts characters, so there is no position to blame
        Assert.False(diagnostic.Span.IsKnown);
    }

    [Fact]
    public void OverTheLimit_ParseThrowsWithoutAPhantomLineZero()
    {
        // a diagnostic with no span used to render as "Line 0, column 0: ..."
        var exception = Assert.Throws<FishboneParseException>(() => ASTParser.Parse(Chain(500)));

        Assert.DoesNotContain("Line 0", exception.Message);
        Assert.DoesNotContain("column 0", exception.Message);
        Assert.StartsWith("This script is nested too deeply", exception.Message);
    }

    [Fact]
    public void ParseStillReportsOrdinarySyntaxErrorsFromTheDeepStackThread()
    {
        // the parse now runs on another thread and its failure is marshalled back. a syntax
        // error has to arrive as itself, not wrapped, or every caller's catch breaks
        var exception = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = 1"));

        Assert.Single(exception.Diagnostics);
        Assert.Contains("Missing ';'", exception.Diagnostics[0].Message);
    }

    [Fact]
    public void ParseStillReportsVisitorErrorsFromTheDeepStackThread()
    {
        // the visitor throws for a literal it cannot represent, and that also crosses the thread
        var exception = Assert.Throws<FishboneParseException>(
            () => ASTParser.Parse("let x = 99999999999999999999999999;"));

        Assert.Contains("too large", exception.Diagnostics[0].Message);
    }

    // --------------------------------------------------------------------------------
    // brackets and operators that are not structure
    // --------------------------------------------------------------------------------

    [Fact]
    public void BracketsInsideAString_DoNotCount()
    {
        string code = "let s = \"" + new string('(', 500) + "\";";

        Assert.False(ParseDepthGuard.LooksTooDeepToParse(code));
    }

    [Fact]
    public void OperatorsInsideAComment_DoNotCount()
    {
        string code = "// " + string.Concat(Enumerable.Repeat("+", 500)) + "\nlet x = 1;";

        Assert.False(ParseDepthGuard.LooksTooDeepToParse(code));
    }

    [Fact]
    public void OperatorsInsideABlockComment_DoNotCount()
    {
        string code = "/* " + string.Concat(Enumerable.Repeat("+", 500)) + " */\nlet x = 1;";

        Assert.False(ParseDepthGuard.LooksTooDeepToParse(code));
    }

    [Fact]
    public void OperatorsSpreadOverManyStatements_DoNotAccumulate()
    {
        // the recursion that matters is one expression's depth, not the whole file's operator
        // count, so a statement boundary has to reset the run
        string code = string.Concat(Enumerable.Repeat("let x = 1 + 2 + 3;\n", 500));

        Assert.False(ParseDepthGuard.LooksTooDeepToParse(code));
    }

    [Fact]
    public void ClosingMoreBracketsThanWereOpened_DoesNotUnderflow()
    {
        Assert.False(ParseDepthGuard.LooksTooDeepToParse(new string(')', 500)));
    }

    [Fact]
    public void AnUnterminatedString_DoesNotSwallowTheRestOfTheFile()
    {
        // while typing, a half-written string is normal. the scanner treats everything after the
        // quote as text, which is the safe direction: it under-counts rather than over-counts
        Assert.False(ParseDepthGuard.LooksTooDeepToParse("let s = \"oops"));
    }
}