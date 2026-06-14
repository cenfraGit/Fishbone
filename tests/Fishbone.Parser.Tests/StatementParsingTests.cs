using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class StatementParsingTests
{
    [Fact]
    public void Parse_DeclarationsAndAssignments_ReturnsExpectedAst()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let x = 5;
x = x + 3 * 2;
let first, second = getValues();
first, second = getValues();
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["x"], new LiteralNode(5)),
            new AssignmentNode(
                ["x"],
                new BinaryOpNode(
                    "+",
                    new IdentifierNode("x"),
                    new BinaryOpNode("*", new LiteralNode(3), new LiteralNode(2))
                )
            ),
            new DeclarationNode(["first", "second"], new FunctionCallNode("getValues", [])),
            new AssignmentNode(["first", "second"], new FunctionCallNode("getValues", []))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_Block_ReturnsNestedBlockNode()
    {
        var ast = ParserTestHelpers.ParseProgram("""
{
    let x = 1;
    x = x + 1;
}
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new BlockNode(new List<AstNode>
            {
                new DeclarationNode(["x"], new LiteralNode(1)),
                new AssignmentNode(
                    ["x"],
                    new BinaryOpNode("+", new IdentifierNode("x"), new LiteralNode(1))
                )
            })
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_ReturnBreakAndContinue_ReturnsControlStatementNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
return;
return x;
return x, y;
break;
continue;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new ReturnNode([]),
            new ReturnNode([new IdentifierNode("x")]),
            new ReturnNode([new IdentifierNode("x"), new IdentifierNode("y")]),
            new BreakNode(),
            new ContinueNode()
        });

        Assert.Equal(expectedAst, ast);
    }
}
