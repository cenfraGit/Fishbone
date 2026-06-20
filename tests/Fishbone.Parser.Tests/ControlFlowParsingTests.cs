using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class ControlFlowParsingTests
{
    [Fact]
    public void Parse_IfElse_ReturnsIfNodeWithElseBlock()
    {
        var ast = ParserTestHelpers.ParseProgram("""
if (score > 80)
{
    grade = 1;
}
else
{
    grade = 3;
}
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IfNode(
                new BinaryOpNode(">", new IdentifierNode("score"), new LiteralNode(80)),
                new BlockNode(new List<AstNode>
                {
                    new AssignmentNode(["grade"], new LiteralNode(1))
                }),
                new BlockNode(new List<AstNode>
                {
                    new AssignmentNode(["grade"], new LiteralNode(3))
                })
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_ElseIfChain_ReturnsNestedIfAst()
    {
        var ast = ParserTestHelpers.ParseProgram("""
if (score > 90)
{
    grade = 1;
}
else if (score > 80)
{
    grade = 2;
}
else if (score > 70)
{
    grade = 3;
}
else
{
    grade = 4;
}
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IfNode(
                new BinaryOpNode(">", new IdentifierNode("score"), new LiteralNode(90)),
                new BlockNode(new List<AstNode>
                {
                    new AssignmentNode(["grade"], new LiteralNode(1))
                }),
                new IfNode(
                    new BinaryOpNode(">", new IdentifierNode("score"), new LiteralNode(80)),
                    new BlockNode(new List<AstNode>
                    {
                        new AssignmentNode(["grade"], new LiteralNode(2))
                    }),
                    new IfNode(
                        new BinaryOpNode(">", new IdentifierNode("score"), new LiteralNode(70)),
                        new BlockNode(new List<AstNode>
                        {
                            new AssignmentNode(["grade"], new LiteralNode(3))
                        }),
                        new BlockNode(new List<AstNode>
                        {
                            new AssignmentNode(["grade"], new LiteralNode(4))
                        })
                    )
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_While_ReturnsWhileNode()
    {
        var ast = ParserTestHelpers.ParseProgram("""
while (x < 5)
{
    x = x + 1;
}
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new WhileNode(
                new BinaryOpNode("<", new IdentifierNode("x"), new LiteralNode(5)),
                new BlockNode(new List<AstNode>
                {
                    new AssignmentNode(
                        ["x"],
                        new BinaryOpNode("+", new IdentifierNode("x"), new LiteralNode(1))
                    )
                })
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_Foreach_ReturnsForeachNode()
    {
        var ast = ParserTestHelpers.ParseProgram("""
foreach (value in values)
{
    total = total + value;
}
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new ForeachNode(
                "value",
                new IdentifierNode("values"),
                new BlockNode(new List<AstNode>
                {
                    new AssignmentNode(
                        ["total"],
                        new BinaryOpNode("+", new IdentifierNode("total"), new IdentifierNode("value"))
                    )
                })
            )
        });

        Assert.Equal(expectedAst, ast);
    }
}
