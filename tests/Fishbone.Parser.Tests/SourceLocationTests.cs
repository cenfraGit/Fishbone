using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class SourceLocationTests
{
    [Fact]
    public void Parse_MultilineIndentedCode_AssignsOneBasedLocations()
    {
        var ast = ParserTestHelpers.ParseProgram("  let value = 42;\n  value = value + 1;");

        AssertLocation(ast, 1, 3);

        var declaration = Assert.IsType<DeclarationNode>(ast.Statements[0]);
        AssertLocation(declaration, 1, 3);
        AssertLocation(declaration.Value, 1, 15);

        var assignment = Assert.IsType<AssignmentNode>(ast.Statements[1]);
        AssertLocation(assignment, 2, 3);

        var expression = Assert.IsType<BinaryOpNode>(assignment.Value);
        AssertLocation(expression, 2, 11);
        AssertLocation(expression.Left, 2, 11);
        AssertLocation(expression.Right, 2, 19);
    }

    [Fact]
    public void Parse_DictionaryPairsAndElseIf_AssignsNestedLocations()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let mapping = {
    "key": 1
};
if (false)
{
}
  else if (true)
{
}
""");

        var declaration = Assert.IsType<DeclarationNode>(ast.Statements[0]);
        var dictionary = Assert.IsType<DictionaryNode>(declaration.Value);
        AssertLocation(dictionary, 1, 15);
        AssertLocation(dictionary.Pairs[0], 2, 5);
        AssertLocation(dictionary.Pairs[0].Key, 2, 5);
        AssertLocation(dictionary.Pairs[0].Value, 2, 12);

        var ifNode = Assert.IsType<IfNode>(ast.Statements[1]);
        AssertLocation(ifNode, 4, 1);

        var elseIfNode = Assert.IsType<IfNode>(ifNode.ElseBranch);
        AssertLocation(elseIfNode, 7, 3);
        AssertLocation(elseIfNode.Condition, 7, 12);
    }

    private static void AssertLocation(AstNode node, int line, int column)
    {
        Assert.Equal(line, node.Line);
        Assert.Equal(column, node.Column);
    }
}