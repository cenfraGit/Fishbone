// --------------------------------------------------------------------------------
// ASTParser.cs
//
// this class holds static methods used to produce the actual AST from
// a string of code, using AstBuilderVisitor.
// --------------------------------------------------------------------------------

using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Fishbone.Core;

namespace Fishbone.Parser;

public class ASTParser
{
    // used to create an AST for the program in the string
    public static AstNode Parse(string code)
    {
        var parser = CreateParser(code, out var errorListener);
        var parseTree = parser.program();

        if (errorListener.Errors.Count > 0)
            throw new FishboneParseException(errorListener.Errors);

        var visitor = new AstBuilderVisitor();
        return visitor.Visit(parseTree);
    }

    // used only for interpolated-string holes in expressions (VisitInterpStringExpr)
    // TODO: may need change? reinvokes lex + parse + error collect pipeline
    // for each interp string, which is lexing work from visitor side?
    public static AstNode ParseExpression(string code)
    {
        var parser = CreateParser(code, out var errorListener);
        var parseTree = parser.exprStandalone();

        if (errorListener.Errors.Count > 0)
            throw new FishboneParseException(errorListener.Errors);

        var visitor = new AstBuilderVisitor();
        return visitor.Visit(parseTree.expr());
    }

    // helper: takes code string and builds char stream/lexer/parser
    // and sets up error listener. reused in Parse and ParseExpression
    private static FishboneParser CreateParser(string code, out CollectingErrorListener errorListener)
    {
        ICharStream charStream = CharStreams.fromString(code);
        var lexer = new FishboneLexer(charStream);
        var parser = new FishboneParser(new CommonTokenStream(lexer));

        // remove default error listeners  and add our error listener
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();
        errorListener = new CollectingErrorListener();
        lexer.AddErrorListener(errorListener);
        parser.AddErrorListener(errorListener);

        return parser;
    }
}