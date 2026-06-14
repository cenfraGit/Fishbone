using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class ExpressionParsingTests
{
    [Fact]
    public void Parse_LiteralExpressions_ReturnsTypedLiteralNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let integerValue = 10;
let floatValue = 3.5;
let stringValue = "hello";
let boolValue = true;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["integerValue"], new LiteralNode(10)),
            new DeclarationNode(["floatValue"], new LiteralNode(3.5)),
            new DeclarationNode(["stringValue"], new LiteralNode("hello")),
            new DeclarationNode(["boolValue"], new LiteralNode(true))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_OperatorPrecedence_MaintainsExpressionStructure()
    {
        var ast = ParserTestHelpers.ParseProgram("let result = 10 + 3 * 4 > -2 == true;");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(
                ["result"],
                new BinaryOpNode(
                    "==",
                    new BinaryOpNode(
                        ">",
                        new BinaryOpNode(
                            "+",
                            new LiteralNode(10),
                            new BinaryOpNode("*", new LiteralNode(3), new LiteralNode(4))
                        ),
                        new UnaryOpNode("-", new LiteralNode(2))
                    ),
                    new LiteralNode(true)
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_Parentheses_OverridesStandardPrecedence()
    {
        var ast = ParserTestHelpers.ParseProgram("let total = (x + y) * 2;");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(
                ["total"],
                new BinaryOpNode(
                    "*",
                    new BinaryOpNode("+", new IdentifierNode("x"), new IdentifierNode("y")),
                    new LiteralNode(2)
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_UnaryNot_ReturnsUnaryOperatorNode()
    {
        var ast = ParserTestHelpers.ParseProgram("let inverted = not basicTrue;");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["inverted"], new UnaryOpNode("not", new IdentifierNode("basicTrue")))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_FunctionCallExpression_ReturnsCallNodeInsideExpression()
    {
        var ast = ParserTestHelpers.ParseProgram("let formatted = formatValue(42, precision);");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(
                ["formatted"],
                new FunctionCallNode(
                    "formatValue",
                    [new LiteralNode(42), new IdentifierNode("precision")]
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }
}
