using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class ParseErrorCollectionTests
{
    [Fact]
    public void Parse_SingleSyntaxError_ThrowsWithOneErrorAndLocation()
    {
        var ex = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = ;"));
        var error = Assert.Single(ex.Errors);
        Assert.Equal(1, error.Line);
        Assert.True(error.Column >= 1);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Parse_MultipleSyntaxErrors_ThrowsWithAllErrors()
    {
        var ex = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = 1\nlet y = ;\nlet z = ;"));
        Assert.True(ex.Errors.Count >= 2);
        Assert.All(ex.Errors, error => Assert.True(error.Line >= 1));
        Assert.All(ex.Errors, error => Assert.True(error.Column >= 1));
    }

    [Fact]
    public void Parse_ValidScript_DoesNotThrow()
    {
        var ast = ASTParser.Parse("let x = 1;");
        Assert.IsType<ProgramNode>(ast);
    }

    [Fact]
    public void Parse_LexerError_UnterminatedString_ThrowsWithError()
    {
        var ex = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = \"unterminated;"));
        Assert.NotEmpty(ex.Errors);
        Assert.All(ex.Errors, error => Assert.True(error.Line >= 1));
    }

    [Fact]
    public void Parse_ColumnIsOneBased()
    {
        var ex = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = ;"));
        var error = Assert.Single(ex.Errors);
        Assert.Equal(1, error.Line);
        Assert.Equal(9, error.Column);
    }

    [Fact]
    public void Parse_ErrorOnSecondLine_ReportsCorrectLine()
    {
        var ex = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = 1;\nlet y = ;"));
        Assert.Contains(ex.Errors, error => error.Line == 2);
    }

    [Fact]
    public void FishboneParseException_DerivesFromException()
    {
        Assert.ThrowsAny<Exception>(() => ASTParser.Parse("let x = ;"));
    }

    [Fact]
    public void Parse_SingleErrorMessage_IncludesLocation()
    {
        var ex = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = ;"));
        Assert.Contains("Line 1", ex.Message);
        Assert.Contains("column", ex.Message.ToLower());
    }

    [Fact]
    public void Parse_MultipleErrors_MessageIncludesCount()
    {
        var ex = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("let x = 1\nlet y = ;\nlet z = ;"));
        Assert.Contains("syntax errors", ex.Message);
    }
}
