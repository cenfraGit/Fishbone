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
            new DeclarationNode(["first", "second"], new CallNode(new IdentifierNode("getValues"), [])),
            new AssignmentNode(["first", "second"], new CallNode(new IdentifierNode("getValues"), []))
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
    public void Parse_IndexedAssignments_ReturnsExpectedAst()
    {
        var ast = ParserTestHelpers.ParseProgram("""
values[0] = 10;
matrix[0][1] = 20;
sample.Items[2] = 30;
getValues()[3] = 40;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IndexedAssignmentNode(new IdentifierNode("values"), new LiteralNode(0), new LiteralNode(10)),
            new IndexedAssignmentNode(
                new IndexingNode(new IdentifierNode("matrix"), new LiteralNode(0)),
                new LiteralNode(1),
                new LiteralNode(20)),
            new IndexedAssignmentNode(
                new MemberAccessNode(new IdentifierNode("sample"), "Items"),
                new LiteralNode(2),
                new LiteralNode(30)),
            new IndexedAssignmentNode(
                new CallNode(new IdentifierNode("getValues"), []),
                new LiteralNode(3),
                new LiteralNode(40))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_NonIndexedExpressionAssignment_Throws()
    {
        Assert.ThrowsAny<Exception>(() => ParserTestHelpers.ParseProgram("(a + b) = 10;"));
    }

    [Fact]
    public void Parse_CompoundAssignment_DesugarsToAssignmentWithBinaryOp()
    {
        var ast = ParserTestHelpers.ParseProgram("""
x += 5;
y *= z - 1;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new AssignmentNode(
                ["x"],
                new BinaryOpNode("+", new IdentifierNode("x"), new LiteralNode(5))),
            new AssignmentNode(
                ["y"],
                new BinaryOpNode(
                    "*",
                    new IdentifierNode("y"),
                    new BinaryOpNode("-", new IdentifierNode("z"), new LiteralNode(1))))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_IndexedCompoundAssignment_DesugarsToIndexedAssignmentWithBinaryOp()
    {
        var ast = ParserTestHelpers.ParseProgram("""
values[1] += 5;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IndexedAssignmentNode(
                new IdentifierNode("values"),
                new LiteralNode(1),
                new BinaryOpNode(
                    "+",
                    new IndexingNode(new IdentifierNode("values"), new LiteralNode(1)),
                    new LiteralNode(5)))
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_CompoundAssignmentToNonAssignableTarget_Throws()
    {
        Assert.ThrowsAny<Exception>(() => ParserTestHelpers.ParseProgram("(a + b) += 1;"));
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