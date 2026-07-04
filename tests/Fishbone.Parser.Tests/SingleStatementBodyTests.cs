using Fishbone.Core;
using System.Collections.Immutable;

namespace Fishbone.Parser.Tests;

public class SingleStatementBodyTests
{
    private static CallNode Call(string name, params AstNode[] args) =>
        new(new IdentifierNode(name), args.Select(a => (ArgumentNode)a).ToImmutableArray());

    [Fact]
    public void Parse_BracelessIf_WrapsBodyInBlock()
    {
        var ast = ParserTestHelpers.ParseProgram("""
if (i % 2 == 0)
    println(i);
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IfNode(
                new BinaryOpNode("==",
                    new BinaryOpNode("%", new IdentifierNode("i"), new LiteralNode(2)),
                    new LiteralNode(0)),
                new BlockNode(new List<AstNode> { Call("println", new IdentifierNode("i")) }),
                null
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_BracelessIfElse_WrapsBothBodies()
    {
        var ast = ParserTestHelpers.ParseProgram("""
if (flag)
    x = 1;
else
    x = 2;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IfNode(
                new IdentifierNode("flag"),
                new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(1)) }),
                new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(2)) })
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_DanglingElse_BindsToNearestIf()
    {
        var ast = ParserTestHelpers.ParseProgram("""
if (a)
    if (b)
        x = 1;
    else
        x = 2;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IfNode(
                new IdentifierNode("a"),
                new BlockNode(new List<AstNode>
                {
                    new IfNode(
                        new IdentifierNode("b"),
                        new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(1)) }),
                        new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(2)) })
                    )
                }),
                null
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_ElseIfSeparatedByNewline_ParsesAsNestedIf()
    {
        var ast = ParserTestHelpers.ParseProgram("""
if (a)
{
    x = 1;
}
else
if (b)
{
    x = 2;
}
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IfNode(
                new IdentifierNode("a"),
                new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(1)) }),
                new IfNode(
                    new IdentifierNode("b"),
                    new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(2)) }),
                    null
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_BracelessWhile_WrapsBodyInBlock()
    {
        var ast = ParserTestHelpers.ParseProgram("""
while (x < 10)
    x += 1;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new WhileNode(
                new BinaryOpNode("<", new IdentifierNode("x"), new LiteralNode(10)),
                new BlockNode(new List<AstNode>
                {
                    new AssignmentNode(["x"],
                        new BinaryOpNode("+", new IdentifierNode("x"), new LiteralNode(1)))
                })
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_BracelessForAndForeach_WrapBodiesInBlocks()
    {
        var ast = ParserTestHelpers.ParseProgram("""
for (i in 0, 5)
    println(i);
foreach (x in xs)
    println(x);
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new ForNode(
                "i",
                new LiteralNode(0),
                new LiteralNode(5),
                null,
                new BlockNode(new List<AstNode> { Call("println", new IdentifierNode("i")) })
            ),
            new ForeachNode(
                "x",
                new IdentifierNode("xs"),
                new BlockNode(new List<AstNode> { Call("println", new IdentifierNode("x")) })
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_BracedElseIfChain_KeepsNestedShape()
    {
        // the shape produced by the old single-token "else if" grammar must be preserved
        var ast = ParserTestHelpers.ParseProgram("""
if (a)
{
    x = 1;
}
else if (b)
{
    x = 2;
}
else
{
    x = 3;
}
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new IfNode(
                new IdentifierNode("a"),
                new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(1)) }),
                new IfNode(
                    new IdentifierNode("b"),
                    new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(2)) }),
                    new BlockNode(new List<AstNode> { new AssignmentNode(["x"], new LiteralNode(3)) })
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }
}