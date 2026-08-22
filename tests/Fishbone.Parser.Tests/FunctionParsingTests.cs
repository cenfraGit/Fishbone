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
                new ReturnNode(new LiteralNode(42))
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
        Assert.All(definition.Parameters, p => Assert.Equal(ArgumentModifier.None, p.Modifier));
        Assert.Equal(
            new BlockNode(new List<AstNode>
            {
                new ReturnNode(
                    new BinaryOpNode("+", new IdentifierNode("left"), new IdentifierNode("right"))
                )
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

    [Fact]
    public void Parse_FunctionDefinitionWithOutAndRefParameters_CapturesDirections()
    {
        var ast = ParserTestHelpers.ParseProgram("""
func divide(numerator, denominator, out quotient, ref remainder)
{
    quotient = 0;
}
""");

        var definition = Assert.IsType<FunctionDefinitionNode>(Assert.Single(ast.Statements));

        Assert.Equal("divide", definition.Name);
        Assert.Equal(
            [new ParameterNode(ArgumentModifier.None, "numerator"),
             new ParameterNode(ArgumentModifier.None, "denominator"),
             new ParameterNode(ArgumentModifier.Out, "quotient"),
             new ParameterNode(ArgumentModifier.Ref, "remainder")],
            definition.Parameters.ToArray());
    }

    [Theory]
    [InlineData("func f(out) { }")]
    [InlineData("func f(a, out, b) { }")]
    [InlineData("func f(ref) { }")]
    public void Parse_ParameterKeywordWithoutName_ThrowsParseException(string code)
    {
        Assert.Throws<FishboneParseException>(() => ASTParser.Parse(code));
    }
}
