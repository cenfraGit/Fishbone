using Fishbone.Core;
using System.Globalization;

namespace Fishbone.Parser.Tests;

public class LiteralParsingTests
{
    private static object ParseSingleLiteral(string code)
    {
        var ast = ParserTestHelpers.ParseProgram(code);
        var literal = Assert.IsType<LiteralNode>(Assert.Single(ast.Statements));
        return literal.Value;
    }

    [Theory]
    [InlineData("""let s = "say \"hi\"";""", "say \"hi\"")]
    [InlineData("""let s = "C:\\Users\\cenic";""", @"C:\Users\cenic")]
    [InlineData("""let s = "line1\nline2";""", "line1\nline2")]
    [InlineData("""let s = "col1\tcol2";""", "col1\tcol2")]
    [InlineData("""let s = "\u0041\u00e9";""", "Aé")]
    [InlineData("""let s = "bell\a null\0";""", "bell\a null\0")]
    public void Parse_EscapeSequences_FollowCSharpConventions(string code, string expected)
    {
        var ast = ParserTestHelpers.ParseProgram(code);
        var declaration = Assert.IsType<DeclarationNode>(Assert.Single(ast.Statements));
        var literal = Assert.IsType<LiteralNode>(declaration.Value);
        Assert.Equal(expected, literal.Value);
    }

    [Theory]
    [InlineData("""let s = "bad \q escape";""")]
    [InlineData("""let s = "short \u12 unicode";""")]
    public void Parse_InvalidEscapeSequence_ThrowsParseException(string code)
    {
        Assert.Throws<FishboneParseException>(() => ASTParser.Parse(code));
    }

    [Fact]
    public void Parse_LiteralLineBreakInString_ThrowsParseException()
    {
        Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let s = \"line1\nline2\";"));
    }

    [Fact]
    public void Parse_NumericLiterals_IgnoreHostCulture()
    {
        // de-DE uses ',' as the decimal separator and '.' for thousands; literal
        // parsing must not depend on the machine's culture
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(3.14, ParseSingleLiteral("3.14;"));
            Assert.Equal(1000000, ParseSingleLiteral("1_000_000;"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}