using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class UnitTest1
{
    [Fact]
    public void Parse_SimpleAssignment_ReturnsCorrectAst()
    {
        string code = "let x = 10;";

        var ast = ASTParser.Parse(code);

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["x"], new LiteralNode(10))
        });

        Console.WriteLine(expectedAst.ToString());
        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_VariableAssignmentAndMath_MaintainsStructure()
    {
        string code = @"
let x = 5;
x = x + 3 * 2;
";

        var ast = ASTParser.Parse(code);

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            // let x = 5;
            new DeclarationNode(["x"], new LiteralNode(5)),

            // x = x + 3 * 2;
            new AssignmentNode(
                new List<string> { "x" },
                new BinaryOpNode(
                    "+",
                    new IdentifierNode("x"),
                    new BinaryOpNode("*", new LiteralNode(3), new LiteralNode(2))
                )
            )
        });

        // ast.PrintAST();
        // expectedAst.PrintAST();

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_UnaryExpressionWithComparison_BindsUnaryTightly()
    {
        string code = "let isLessThan = x > -y;";

        var ast = ASTParser.Parse(code);

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(
                ["isLessThan"],
                new BinaryOpNode(
                    ">",
                    new IdentifierNode("x"),
                    new UnaryOpNode("-", new IdentifierNode("y"))
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_Parentheses_OverridesStandardPrecedence()
    {
        string code = "let total = (x + y) * 2;";

        var ast = ASTParser.Parse(code);

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
}