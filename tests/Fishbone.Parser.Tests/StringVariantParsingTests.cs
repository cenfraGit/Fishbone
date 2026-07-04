using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class StringVariantParsingTests
{
    private static object ParseDeclarationValue(string code)
    {
        var ast = ParserTestHelpers.ParseProgram(code);
        var declaration = Assert.IsType<DeclarationNode>(Assert.Single(ast.Statements));
        return declaration.Value;
    }

    // ------------------------------------------------------------ raw strings

    [Fact]
    public void Parse_RawString_KeepsBackslashesVerbatim()
    {
        var value = Assert.IsType<LiteralNode>(ParseDeclarationValue(
            """let p = @"C:\Users\me\new";"""));
        Assert.Equal(@"C:\Users\me\new", value.Value);
    }

    [Fact]
    public void Parse_RawString_DoubledQuoteIsLiteralQuote()
    {
        var value = Assert.IsType<LiteralNode>(ParseDeclarationValue(
            """"let s = @"say ""hi""";""""));
        Assert.Equal("say \"hi\"", value.Value);
    }

    [Fact]
    public void Parse_RawString_MaySpanLines()
    {
        var value = Assert.IsType<LiteralNode>(ParseDeclarationValue(
            "let s = @\"line1\nline2\";"));
        Assert.Equal("line1\nline2", value.Value);
    }

    // ------------------------------------------------------------ interpolated strings

    [Fact]
    public void Parse_InterpolatedString_SplitsLiteralAndHoleParts()
    {
        var value = Assert.IsType<InterpolatedStringNode>(ParseDeclarationValue(
            """let s = $"hello {name}!";"""));

        var expected = new InterpolatedStringNode(
        [
            new LiteralNode("hello "),
            new IdentifierNode("name"),
            new LiteralNode("!"),
        ]);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Parse_InterpolatedString_HoleHoldsFullExpression()
    {
        var value = Assert.IsType<InterpolatedStringNode>(ParseDeclarationValue(
            """let s = $"sum: {a + b * 2}";"""));

        var expected = new InterpolatedStringNode(
        [
            new LiteralNode("sum: "),
            new BinaryOpNode("+",
                new IdentifierNode("a"),
                new BinaryOpNode("*", new IdentifierNode("b"), new LiteralNode(2))),
        ]);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Parse_InterpolatedString_HoleMayContainStringsAndNestedBraces()
    {
        var value = Assert.IsType<InterpolatedStringNode>(ParseDeclarationValue(
            """let s = $"v={d["key"]} w={ {"a": 1}["a"] }";"""));

        Assert.Equal(4, value.Parts.Length);
        Assert.Equal(new IndexingNode(new IdentifierNode("d"), new LiteralNode("key")), value.Parts[1]);
        var nested = Assert.IsType<IndexingNode>(value.Parts[3]);
        Assert.IsType<DictionaryNode>(nested.Target);
    }

    [Fact]
    public void Parse_InterpolatedString_DoubledBracesAreLiterals()
    {
        var value = Assert.IsType<InterpolatedStringNode>(ParseDeclarationValue(
            """let s = $"{{x}} and {{{y}}}";"""));

        var expected = new InterpolatedStringNode(
        [
            new LiteralNode("{x} and {"),
            new IdentifierNode("y"),
            new LiteralNode("}"),
        ]);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Parse_InterpolatedString_EscapesWorkInLiteralParts()
    {
        var value = Assert.IsType<InterpolatedStringNode>(ParseDeclarationValue(
            """let s = $"a\t{x}\n";"""));

        var expected = new InterpolatedStringNode(
        [
            new LiteralNode("a\t"),
            new IdentifierNode("x"),
            new LiteralNode("\n"),
        ]);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Parse_InterpolatedString_WithoutHoles_IsSingleLiteralPart()
    {
        var value = Assert.IsType<InterpolatedStringNode>(ParseDeclarationValue(
            """let s = $"plain";"""));
        Assert.Equal(new LiteralNode("plain"), Assert.Single(value.Parts));
    }

    [Fact]
    public void Parse_EmptyInterpolationHole_ThrowsParseException()
    {
        var exception = Assert.Throws<FishboneParseException>(
            () => ASTParser.Parse("""let s = $"bad {} hole";"""));
        Assert.Contains("Empty interpolation hole", exception.Message);
    }

    [Fact]
    public void Parse_HoleExpression_ReportsPositionsInOriginalScript()
    {
        // the hole starts at column 17 (1-based) on line 2
        var ast = ParserTestHelpers.ParseProgram("let a = 1;\nlet s = $\"value {a + b}\";");
        var declaration = Assert.IsType<DeclarationNode>(ast.Statements[1]);
        var interpolated = Assert.IsType<InterpolatedStringNode>(declaration.Value);
        var hole = Assert.IsType<BinaryOpNode>(interpolated.Parts[1]);

        Assert.Equal(2, hole.Line);
        Assert.Equal(18, hole.Column);
    }
}