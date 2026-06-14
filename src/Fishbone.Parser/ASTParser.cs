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
        var parseTree = parser.program();
        var visitor = new AstBuilderVisitor();
        var rootAst = visitor.Visit(parseTree);
        return rootAst;
    }
}