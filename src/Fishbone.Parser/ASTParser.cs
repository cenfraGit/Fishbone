using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Fishbone.Core;

namespace Fishbone.Parser;

public class ASTParser
{
    public static AstNode Parse(string code)
    {
        ICharStream charStream = CharStreams.fromString(code);
        var lexer = new FishboneLexer(charStream);
        var parser = new FishboneParser(new CommonTokenStream(lexer));

        var errorListener = new CollectingErrorListener();
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        lexer.AddErrorListener(errorListener);
        parser.AddErrorListener(errorListener);

        var parseTree = parser.program();

        if (errorListener.Errors.Count > 0)
            throw new FishboneParseException(errorListener.Errors);

        var visitor = new AstBuilderVisitor();
        return visitor.Visit(parseTree);
    }
}