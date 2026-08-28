using Fishbone;
using Fishbone.Ast;

namespace Fishbone.Parser.Tests;

public class TryCatchParsingTests
{
    [Fact]
    public void Parse_TryCatchWithBinding_ReturnsTryNode()
    {
        var ast = ParserTestHelpers.ParseProgram("""
try { x = 1; } catch (e) { y = 2; }
""");

        var expectedAst = new ProgramNode(new List<AstNode>
        {
            new TryNode(
                new BlockNode(new List<AstNode> { new AssignmentNode("x", new LiteralNode(1)) }),
                "e",
                new BlockNode(new List<AstNode> { new AssignmentNode("y", new LiteralNode(2)) }),
                null)
        });

        Assert.Equal(expectedAst, ast);
    }

    [Fact]
    public void Parse_TryCatchWithoutBinding_HasNullExceptionName()
    {
        var ast = ParserTestHelpers.ParseProgram("""try { } catch { }""");
        var tryNode = Assert.IsType<TryNode>(Assert.Single(ast.Statements));

        Assert.Null(tryNode.ExceptionName);
        Assert.NotNull(tryNode.CatchBlock);
        Assert.Null(tryNode.FinallyBlock);
    }

    [Fact]
    public void Parse_TryFinally_HasNoCatchBlock()
    {
        var ast = ParserTestHelpers.ParseProgram("""try { } finally { x = 1; }""");
        var tryNode = Assert.IsType<TryNode>(Assert.Single(ast.Statements));

        Assert.Null(tryNode.CatchBlock);
        Assert.NotNull(tryNode.FinallyBlock);
    }

    [Fact]
    public void Parse_TryCatchFinally_HasAllBlocks()
    {
        var ast = ParserTestHelpers.ParseProgram("""try { } catch (e) { } finally { }""");
        var tryNode = Assert.IsType<TryNode>(Assert.Single(ast.Statements));

        Assert.Equal("e", tryNode.ExceptionName);
        Assert.NotNull(tryNode.CatchBlock);
        Assert.NotNull(tryNode.FinallyBlock);
    }

    [Fact]
    public void Parse_BareTry_ThrowsParseException()
    {
        var exception = Assert.Throws<FishboneParseException>(() => ASTParser.Parse("""try { x = 1; }"""));
        Assert.Contains("requires a 'catch' or a 'finally'", exception.Message);
    }

    [Fact]
    public void Parse_ThrowWithValueAndBareRethrow_ReturnThrowNodes()
    {
        var ast = ParserTestHelpers.ParseProgram("""
throw "boom";
try { } catch { throw; }
""");

        var throwNode = Assert.IsType<ThrowNode>(ast.Statements[0]);
        Assert.Equal(new LiteralNode("boom"), throwNode.Value);

        var tryNode = Assert.IsType<TryNode>(ast.Statements[1]);
        var rethrow = Assert.IsType<ThrowNode>(Assert.Single(tryNode.CatchBlock!.Statements));
        Assert.Null(rethrow.Value);
    }
}