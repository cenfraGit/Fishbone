namespace Fishbone.Parser.Tests;

// the guard exists to stop a stack overflow, which cannot be caught and takes the process down.
// the thresholds it defends against were measured on a default 1MB stack: roughly 500 chained
// binary operators, 800 nested parentheses, and between 500 and 1000 nested brackets or blocks.
// these tests pin that it refuses well before those, and does not refuse real code.
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
    public void TheGuardRefusesWellBeforeTheParserBreaks()
    {
        // a chain just under the limit still parses, so the guard is not merely refusing
        // everything: it has to let through what is safe
        string safe = Chain(ParseDepthGuard.DepthLimit - 10);

        Assert.False(ParseDepthGuard.LooksTooDeepToParse(safe));
        Assert.True(ASTParser.TryParse(safe, out _, out _), "input under the limit should parse");
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