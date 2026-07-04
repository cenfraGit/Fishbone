using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class CastParsingTests
{
    [Fact]
    public void Parse_CastExpression_ReturnsCastNode()
    {
        var ast = ParserTestHelpers.ParseProgram("""let n = value as int;""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["n"], new CastNode(new IdentifierNode("value"), "int"))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_CastBindsLooserThanAdditive_AndTighterThanComparison()
    {
        var ast = ParserTestHelpers.ParseProgram("""let ok = a + b as double < c;""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["ok"],
                new BinaryOpNode("<",
                    new CastNode(
                        new BinaryOpNode("+", new IdentifierNode("a"), new IdentifierNode("b")),
                        "double"),
                    new IdentifierNode("c")))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_ChainedCasts_NestLeftToRight()
    {
        var ast = ParserTestHelpers.ParseProgram("""let x = v as double as string;""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["x"],
                new CastNode(new CastNode(new IdentifierNode("v"), "double"), "string"))
        });

        Assert.Equal(expectedAst, ast);
    }
}