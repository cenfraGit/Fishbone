using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Fishbone.Core;

namespace Fishbone.Parser;

public class ASTParser
{
    public static AstNode Parse(string code)
    {
        var parser = CreateParser(code, out var errorListener);
        var parseTree = parser.program();

        if (errorListener.Errors.Count > 0)
            throw new FishboneParseException(errorListener.Errors);

        var visitor = new AstBuilderVisitor();
        return visitor.Visit(parseTree);
    }

    /// <summary>Parses a single expression (used for interpolated-string holes).</summary>
    public static AstNode ParseExpression(string code)
    {
        var parser = CreateParser(code, out var errorListener);
        var parseTree = parser.exprStandalone();

        if (errorListener.Errors.Count > 0)
            throw new FishboneParseException(errorListener.Errors);

        var visitor = new AstBuilderVisitor();
        return visitor.Visit(parseTree.expr());
    }

    private static FishboneParser CreateParser(string code, out CollectingErrorListener errorListener)
    {
        ICharStream charStream = CharStreams.fromString(code);
        var lexer = new FishboneLexer(charStream);
        var parser = new FishboneParser(new CommonTokenStream(lexer));

        errorListener = new CollectingErrorListener();
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        parser.AddErrorListener(errorListener);
        return parser;
    }
}