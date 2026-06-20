using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class FunctionParsingTests
{
    [Fact]
    public void Parse_FunctionDefinitionWithoutParameters_ReturnsDefinitionNode()
    {
        var ast = ParserTestHelpers.ParseProgram("""
func getAnswer()
{
    return 42;
}
""");

        var function = Assert.Single(ast.Statements);
        var definition = Assert.IsType<FunctionDefinitionNode>(function);

        Assert.Equal("getAnswer", definition.Name);
        Assert.Empty(definition.Parameters);
        Assert.Equal(
            new BlockNode(new List<AstNode>
            {
                new ReturnNode([new LiteralNode(42)])
            }),
            definition.Body
        );
    }

    [Fact]
    public void Parse_FunctionDefinitionWithParameters_ReturnsDefinitionNode()
    {
        var ast = ParserTestHelpers.ParseProgram("""
func add(left, right)
{
    return left + right;
}
""");

        var function = Assert.Single(ast.Statements);
        var definition = Assert.IsType<FunctionDefinitionNode>(function);

        Assert.Equal("add", definition.Name);
        Assert.Equal(["left", "right"], definition.Parameters.ToArray());
        Assert.Equal(
            new BlockNode(new List<AstNode>
            {
                new ReturnNode([
                    new BinaryOpNode("+", new IdentifierNode("left"), new IdentifierNode("right"))
                ])
            }),
            definition.Body
        );
    }

    [Fact]
    public void Parse_FunctionCallStatements_ReturnCallNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
reset();
record(1, value);
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new CallNode(new IdentifierNode("reset"), []),
            new CallNode(new IdentifierNode("record"), [new LiteralNode(1), new IdentifierNode("value")])
        });

        Assert.Equal(expectedAst, ast);
    }
}
