using Fishbone.Core;

namespace Fishbone.Parser.Tests;

public class ExpressionParsingTests
{
    [Fact]
    public void Parse_CallArguments_CaptureOutRefAndValueModifiers()
    {
        var ast = ParserTestHelpers.ParseProgram("foo(out x, ref y, z);");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new CallNode(new IdentifierNode("foo"),
            [
                new ArgumentNode(ArgumentModifier.Out, new IdentifierNode("x")),
                new ArgumentNode(ArgumentModifier.Ref, new IdentifierNode("y")),
                new ArgumentNode(ArgumentModifier.None, new IdentifierNode("z"))
            ])
        });

        Assert.Equal(expectedAst, ast);
    }

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
    public void Parse_BooleanOperators_ReturnsBinaryOperatorNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let conjunction = left and right;
let disjunction = left or right;
let exclusive = left xor right;
let combined = a < b and c != d;
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(
                ["conjunction"],
                new BinaryOpNode("and", new IdentifierNode("left"), new IdentifierNode("right"))
            ),
            new DeclarationNode(
                ["disjunction"],
                new BinaryOpNode("or", new IdentifierNode("left"), new IdentifierNode("right"))
            ),
            new DeclarationNode(
                ["exclusive"],
                new BinaryOpNode("xor", new IdentifierNode("left"), new IdentifierNode("right"))
            ),
            new DeclarationNode(
                ["combined"],
                new BinaryOpNode(
                    "and",
                    new BinaryOpNode("<", new IdentifierNode("a"), new IdentifierNode("b")),
                    new BinaryOpNode("!=", new IdentifierNode("c"), new IdentifierNode("d"))
                )
            )
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
                new CallNode(
                    new IdentifierNode("formatValue"),
                    [new LiteralNode(42), new IdentifierNode("precision")]
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_ListExpressions_ReturnsListNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let empty = [];
let mixed = [1, "two", true];
let expressions = [x, x + 1, format(42)];
let nested = [[1, 2], [3, 4]];
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["empty"], new ListNode([])),
            new DeclarationNode(
                ["mixed"],
                new ListNode([
                    new LiteralNode(1),
                    new LiteralNode("two"),
                    new LiteralNode(true)
                ])
            ),
            new DeclarationNode(
                ["expressions"],
                new ListNode([
                    new IdentifierNode("x"),
                    new BinaryOpNode("+", new IdentifierNode("x"), new LiteralNode(1)),
                    new CallNode(new IdentifierNode("format"), [new LiteralNode(42)])
                ])
            ),
            new DeclarationNode(
                ["nested"],
                new ListNode([
                    new ListNode([new LiteralNode(1), new LiteralNode(2)]),
                    new ListNode([new LiteralNode(3), new LiteralNode(4)])
                ])
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_DictionaryExpressions_ReturnsDictionaryNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let empty = {};
let mixed = {"one": 1, 2: "two", true: false};
let expressions = {key: x + 1, format(42): value};
let nested = {"list": [1, 2], "dict": {"inner": 3}};
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["empty"], new DictionaryNode([])),
            new DeclarationNode(
                ["mixed"],
                new DictionaryNode([
                    new KeyValuePairNode(new LiteralNode("one"), new LiteralNode(1)),
                    new KeyValuePairNode(new LiteralNode(2), new LiteralNode("two")),
                    new KeyValuePairNode(new LiteralNode(true), new LiteralNode(false))
                ])
            ),
            new DeclarationNode(
                ["expressions"],
                new DictionaryNode([
                    new KeyValuePairNode(
                        new IdentifierNode("key"),
                        new BinaryOpNode("+", new IdentifierNode("x"), new LiteralNode(1))
                    ),
                    new KeyValuePairNode(
                        new CallNode(new IdentifierNode("format"), [new LiteralNode(42)]),
                        new IdentifierNode("value")
                    )
                ])
            ),
            new DeclarationNode(
                ["nested"],
                new DictionaryNode([
                    new KeyValuePairNode(
                        new LiteralNode("list"),
                        new ListNode([new LiteralNode(1), new LiteralNode(2)])
                    ),
                    new KeyValuePairNode(
                        new LiteralNode("dict"),
                        new DictionaryNode([
                            new KeyValuePairNode(new LiteralNode("inner"), new LiteralNode(3))
                        ])
                    )
                ])
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_IndexingExpressions_ReturnsIndexingNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let first = values[0];
let keyed = values["name"];
let nested = matrix[0][1];
let expressionIndex = values[i + 1];
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(
                ["first"],
                new IndexingNode(new IdentifierNode("values"), new LiteralNode(0))
            ),
            new DeclarationNode(
                ["keyed"],
                new IndexingNode(new IdentifierNode("values"), new LiteralNode("name"))
            ),
            new DeclarationNode(
                ["nested"],
                new IndexingNode(
                    new IndexingNode(new IdentifierNode("matrix"), new LiteralNode(0)),
                    new LiteralNode(1)
                )
            ),
            new DeclarationNode(
                ["expressionIndex"],
                new IndexingNode(
                    new IdentifierNode("values"),
                    new BinaryOpNode("+", new IdentifierNode("i"), new LiteralNode(1))
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_MemberAccessExpressions_ReturnsMemberAccessNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
let property = obj.Name;
let chain = obj.Child.Name;
let indexed = obj.Items[0].Name;
let called = obj.GetChild().Name;
let method = obj.Resize(100, 200);
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(
                ["property"],
                new MemberAccessNode(new IdentifierNode("obj"), "Name")
            ),
            new DeclarationNode(
                ["chain"],
                new MemberAccessNode(
                    new MemberAccessNode(new IdentifierNode("obj"), "Child"),
                    "Name"
                )
            ),
            new DeclarationNode(
                ["indexed"],
                new MemberAccessNode(
                    new IndexingNode(
                        new MemberAccessNode(new IdentifierNode("obj"), "Items"),
                        new LiteralNode(0)
                    ),
                    "Name"
                )
            ),
            new DeclarationNode(
                ["called"],
                new MemberAccessNode(
                    new CallNode(
                        new MemberAccessNode(new IdentifierNode("obj"), "GetChild"),
                        []
                    ),
                    "Name"
                )
            ),
            new DeclarationNode(
                ["method"],
                new CallNode(
                    new MemberAccessNode(new IdentifierNode("obj"), "Resize"),
                    [new LiteralNode(100), new LiteralNode(200)]
                )
            )
        });

        Assert.Equal(expectedAst, ast);
    }
}