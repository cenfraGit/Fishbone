// --------------------------------------------------------------------------------
// ASTParser.cs
//
// this class holds static methods used to produce the actual AST from
// a string of code, using AstBuilderVisitor.
//
// parses are serialized. antlr's generated parser and lexer share one DFA array and
// one PredictionContextCache across every instance in the process (static fields in
// the generated FishboneParser), and those are mutated as the ATN warms up. whether
// the C# runtime makes that safe under concurrency is not something this repo can
// verify, and it stopped being hypothetical once the ide began parsing in the
// background on every typing pause while a run could be parsing at the same time.
// a parse is single-digit milliseconds warm, so serializing them costs little; a host
// wanting genuine parallel throughput would need per-instance ATN caches instead,
// which trades the warm-DFA speedup away on every call.
// --------------------------------------------------------------------------------

using Antlr4.Runtime;
using Fishbone.Core;

namespace Fishbone.Parser;

public static class ASTParser
{
    // re-entrant by design: the visitor calls back into ParseExpression for each
    // interpolated-string hole while this is already held
    private static readonly object ParseGate = new();

    // used to create an AST for the program in the string
    public static AstNode Parse(string code)
    {
        if (!TryParse(code, out var ast, out var diagnostics))
            throw new FishboneParseException(diagnostics);

        return ast!;
    }

    /// <summary>
    /// Parses without throwing for a bad script, handing back the syntax errors instead. Intended
    /// for a caller that expects invalid input to be normal, such as an editor validating as the
    /// user types, where throwing on nearly every keystroke is both wasteful and makes
    /// break-on-all-exceptions unusable. <see cref="Parse"/> is this plus a throw.
    /// </summary>
    /// <returns>True when <paramref name="ast"/> was produced; false when the source has errors.</returns>
    public static bool TryParse(string code, out AstNode? ast, out IReadOnlyList<FishboneDiagnostic> diagnostics)
    {
        lock (ParseGate)
            return TryParseCore(code, out ast, out diagnostics);
    }

    private static bool TryParseCore(string code, out AstNode? ast, out IReadOnlyList<FishboneDiagnostic> diagnostics)
    {
        var parser = CreateParser(code, out var errorListener);
        var parseTree = parser.program();

        // the visitor is not safe against a broken parse tree (ParseSpans.Span already has to
        // special-case a rule with no Stop token), so a failed parse returns before it runs
        if (errorListener.Diagnostics.Count > 0)
        {
            ast = null;
            diagnostics = errorListener.Diagnostics;
            return false;
        }

        try
        {
            ast = new AstBuilderVisitor().Visit(parseTree);
        }
        catch (FishboneParseException exception)
        {
            // the visitor diagnoses things the grammar cannot, like a literal too large for its
            // type or an unrecognized escape, and reports them by throwing
            ast = null;
            diagnostics = exception.Diagnostics;
            return false;
        }

        diagnostics = [];
        return true;
    }

    // used only for interpolated-string holes in expressions (VisitInterpStringExpr)
    // TODO: may need change? reinvokes lex + parse + error collect pipeline
    // for each interp string, which is lexing work from visitor side?
    internal static AstNode ParseExpression(string code)
    {
        var parser = CreateParser(code, out var errorListener);
        var parseTree = parser.exprStandalone();

        if (errorListener.Diagnostics.Count > 0)
            throw new FishboneParseException(errorListener.Diagnostics);

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