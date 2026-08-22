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
        {
            // the deep stack raises the parser's ceiling but the interpreter still recurses on
            // the caller's stack, so anything past the limit is refused rather than risking an
            // uncatchable overflow later
            if (ParseDepthGuard.LooksTooDeepToParse(code))
            {
                ast = null;
                diagnostics = [TooDeeplyNested()];
                return false;
            }

            // out parameters cannot be captured by the lambda, so the core hands back a tuple
            (bool parsed, AstNode? node, IReadOnlyList<FishboneDiagnostic> found) =
                DeepStackRunner.Run(() => TryParseCore(code));

            ast = node;
            diagnostics = found;
            return parsed;
        }
    }

    private static FishboneDiagnostic TooDeeplyNested() =>
        new(DiagnosticStage.Parse,
            DiagnosticSeverity.Error,
            "This script is nested too deeply to run safely. Try splitting the expression " +
            "across several statements.",
            // the check counts characters and has no single position to blame
            SourceSpan.None);

    private static (bool Parsed, AstNode? Ast, IReadOnlyList<FishboneDiagnostic> Diagnostics) TryParseCore(string code)
    {
        var parser = CreateParser(code, out var errorListener);
        var parseTree = parser.program();

        // the visitor is not safe against a broken parse tree (ParseSpans.Span already has to
        // special-case a rule with no Stop token), so a failed parse returns before it runs
        if (errorListener.Diagnostics.Count > 0)
            return (false, null, errorListener.Diagnostics);

        AstNode ast;
        try
        {
            ast = new AstBuilderVisitor().Visit(parseTree);
        }
        catch (FishboneParseException exception)
        {
            // the visitor diagnoses things the grammar cannot, like a literal too large for its
            // type or an unrecognized escape, and reports them by throwing
            return (false, null, exception.Diagnostics);
        }

        return (true, ast, []);
    }

    // used only for interpolated-string holes in expressions (VisitInterpStringExpr).
    // startLine/startColumn place the fragment where it really sits in the enclosing file, so the
    // spans it produces need no adjustment afterwards. this used to be done by prefixing the
    // fragment with that many newlines and spaces, which meant lexing the padding on every hole
    // and made a hole's cost grow with how far down the file it was
    internal static AstNode ParseExpression(string code, int startLine = 1, int startColumn = 1)
    {
        var parser = CreateParser(code, out var errorListener, startLine, startColumn);
        var parseTree = parser.exprStandalone();

        if (errorListener.Diagnostics.Count > 0)
            throw new FishboneParseException(errorListener.Diagnostics);

        var visitor = new AstBuilderVisitor();
        return visitor.Visit(parseTree.expr());
    }

    // helper: takes code string and builds char stream/lexer/parser
    // and sets up error listener. reused in Parse and ParseExpression.
    //
    // startLine/startColumn tell the lexer where this text begins in the file it came from, which
    // only matters when parsing a fragment (an interpolation hole). safe to set here because the
    // token stream is built but not filled: filling happens inside the parser rule call
    private static FishboneParser CreateParser(string code, out CollectingErrorListener errorListener,
                                               int startLine = 1, int startColumn = 1)
    {
        ICharStream charStream = CharStreams.fromString(code);
        var lexer = new FishboneLexer(charStream);

        // antlr counts columns from 0 while fishbone counts from 1, hence the -1
        lexer.Line = startLine;
        lexer.Column = startColumn - 1;

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