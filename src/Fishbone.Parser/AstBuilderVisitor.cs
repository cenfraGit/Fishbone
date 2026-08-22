// --------------------------------------------------------------------------------
// AstBuilderVisitor.cs
//
// this class builds the actual AST for a given program. it takes the
// FishboneParser elements produced by ANTLR and traverses them to
// build the AST (normally starting with ProgramContext)
// --------------------------------------------------------------------------------

using Fishbone.Core;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Fishbone.Parser;

internal sealed class AstBuilderVisitor : FishboneBaseVisitor<AstNode>
{
    public override AstNode VisitProgram(FishboneParser.ProgramContext context)
    {
        var statements = new List<AstNode>();

        foreach (var statement in context.statement())
            statements.Add(Visit(statement));

        return new ProgramNode(statements) { Span = context.Span() };
    }

    public override AstNode VisitBlockStat(FishboneParser.BlockStatContext context)
    {
        var statements = new List<AstNode>();

        foreach (var statement in context.statement())
            statements.Add(Visit(statement));

        return new BlockNode(statements) { Span = context.Span() };
    }

    public override AstNode VisitFunctionDefinitionStat(FishboneParser.FunctionDefinitionStatContext context)
    {
        var funcName = context.ID().GetText();
        var block = Visit(context.blockStat());

        // get parameters, each optionally marked 'out' or 'ref'
        var funcParams = new List<ParameterNode>();
        foreach (var parameter in context.parameter())
        {
            var modifier = parameter.OUT() is not null ? ArgumentModifier.Out
                : parameter.REF() is not null ? ArgumentModifier.Ref
                : ArgumentModifier.None;

            funcParams.Add(new ParameterNode(modifier, parameter.ID().GetText()));
        }

        return new FunctionDefinitionNode(funcName, funcParams.ToImmutableArray(), (BlockNode)block) { Span = context.Span() };
    }

    public override AstNode VisitCallExpr(FishboneParser.CallExprContext context)
    {
        var callee = Visit(context.expr());
        var funcArgs = new List<ArgumentNode>();

        foreach (var argument in context.argument())
        {
            var modifier = argument.OUT() is not null ? ArgumentModifier.Out
                : argument.REF() is not null ? ArgumentModifier.Ref
                : ArgumentModifier.None;

            funcArgs.Add(new ArgumentNode(modifier, Visit(argument.expr())));
        }

        return new CallNode(callee, funcArgs.ToImmutableArray()) { Span = context.Span() };
    }

    public override AstNode VisitReturnStat(FishboneParser.ReturnStatContext context)
    {
        // a bare "return;" carries no expression
        var value = context.expr() is null ? null : Visit(context.expr());
        return new ReturnNode(value) { Span = context.Span() };
    }

    public override AstNode VisitTryStat(FishboneParser.TryStatContext context)
    {
        var line = context.Start.Line;
        var column = context.Start.Column + 1;
        var span = context.Span();

        var catchClause = context.catchClause();
        var finallyClause = context.finallyClause();
        if (catchClause is null && finallyClause is null)
            throw new FishboneParseException([new ParseError(line, column,
                "A 'try' statement requires a 'catch' or a 'finally' clause.", "try") { Span = span }]);

        var tryBlock = (BlockNode)Visit(context.blockStat());
        string? exceptionName = catchClause?.ID()?.GetText();
        var catchBlock = catchClause is null ? null : (BlockNode)Visit(catchClause.blockStat());
        var finallyBlock = finallyClause is null ? null : (BlockNode)Visit(finallyClause.blockStat());

        return new TryNode(tryBlock, exceptionName, catchBlock, finallyBlock) { Span = span };
    }

    public override AstNode VisitThrowStat(FishboneParser.ThrowStatContext context)
    {
        var value = context.expr() is null ? null : Visit(context.expr());
        return new ThrowNode(value) { Span = context.Span() };
    }

    public override AstNode VisitBreakStat(FishboneParser.BreakStatContext context)
    {
        return new BreakNode() { Span = context.Span() };
    }

    public override AstNode VisitContinueStat(FishboneParser.ContinueStatContext context)
    {
        return new ContinueNode() { Span = context.Span() };
    }

    public override AstNode VisitStatement(FishboneParser.StatementContext context)
    {
        return Visit(context.GetChild(0));
    }

    public override AstNode VisitParenthesesExpr(FishboneParser.ParenthesesExprContext context)
    {
        var innerExpr = Visit(context.expr());
        return innerExpr;
    }

    public override AstNode VisitListExpr(FishboneParser.ListExprContext context)
    {
        var elements = new List<AstNode>();
        for (int i = 0; i < context.expr().Length; i++)
            elements.Add(Visit(context.expr(i)));

        return new ListNode(elements.ToImmutableArray()) { Span = context.Span() };
    }

    public override AstNode VisitDictionaryExpr(FishboneParser.DictionaryExprContext context)
    {
        var keyValuePairs = new List<KeyValuePairNode>();
        for (int i = 0; i < context.dictPair().Count(); i++)
        {
            var dictPair = context.dictPair(i);
            var key = Visit(dictPair.expr(0));
            var value = Visit(dictPair.expr(1));
            keyValuePairs.Add(new KeyValuePairNode(key, value) { Span = dictPair.Span() });
        }
        return new DictionaryNode(keyValuePairs.ToImmutableArray()) { Span = context.Span() };
    }

    public override AstNode VisitIndexingExpr(FishboneParser.IndexingExprContext context)
    {
        var target = Visit(context.expr(0));
        var index = Visit(context.expr(1));
        return new IndexingNode(target, index) { Span = context.Span() };
    }

    public override AstNode VisitMemberAccessExpr(FishboneParser.MemberAccessExprContext context)
    {
        var target = Visit(context.expr());
        var id = context.ID().GetText();
        return new MemberAccessNode(target, id) { Span = context.Span() };
    }

    public override AstNode VisitDeclarationStat(FishboneParser.DeclarationStatContext context)
    {
        var name = context.ID().GetText();
        AstNode value = Visit(context.expr());
        return new DeclarationNode(name, value) { Span = context.Span() };
    }

    // every statement that begins with an expression arrives here: a bare expression, a plain
    // assignment, and a compound assignment. the grammar keeps them together so it can commit
    // early; deciding which one this is, and whether the target can be assigned to, is this
    // method's job
    public override AstNode VisitExprStatement(FishboneParser.ExprStatementContext context)
    {
        // visited once. the target's own subexpressions end up inside the node that is built
        // from it, so they are still evaluated exactly once at run time
        AstNode target = Visit(context.expr(0));
        var span = context.Span();

        // no operator, so the statement is just the expression
        if (context.expr().Length == 1)
            return target;

        AstNode rightValue = Visit(context.expr(1));
        string op = context.GetChild(1).GetText();

        if (op == "=")
            return target switch
            {
                IdentifierNode identifier => new AssignmentNode(identifier.Name, rightValue) { Span = span },
                IndexingNode indexing =>
                    new IndexedAssignmentNode(indexing.Target, indexing.Index, rightValue) { Span = span },
                _ => throw Rejected("Indexed assignment requires an indexed target", target, context, span)
            };

        // "target <op>= right" converts to "target = target <op> right".
        string binaryOp = op[..^1];
        return target switch
        {
            IdentifierNode identifier => new AssignmentNode(
                identifier.Name,
                new BinaryOpNode(binaryOp, identifier, rightValue) { Span = span }) { Span = span },
            IndexingNode indexing => new IndexedAssignmentNode(
                indexing.Target,
                indexing.Index,
                new BinaryOpNode(binaryOp, indexing, rightValue) { Span = span }) { Span = span },
            _ => throw Rejected("Compound assignment requires a variable or indexed target", target, context, span)
        };
    }

    private static FishboneParseException Rejected(
        string reason, AstNode target, FishboneParser.ExprStatementContext context, SourceSpan span) =>
        new([new ParseError(span.Line, span.Column,
            $"{reason}, but found {target.GetType().Name}.", context.expr(0).GetText()) { Span = span }]);

    public override AstNode VisitUnaryExpr(FishboneParser.UnaryExprContext context)
    {
        string op = context.GetChild(0).GetText();
        AstNode right = Visit(context.expr());
        return new UnaryOpNode(op, right) { Span = context.Span() };
    }

    public override AstNode VisitBinaryExpr(FishboneParser.BinaryExprContext context)
    {
        AstNode left = Visit(context.expr(0));
        AstNode right = Visit(context.expr(1));
        string op = context.GetChild(1).GetText();
        return new BinaryOpNode(op, left, right) { Span = context.Span() };
    }

    public override AstNode VisitBoolOperatorExpr(FishboneParser.BoolOperatorExprContext context)
    {
        AstNode left = Visit(context.expr(0));
        AstNode right = Visit(context.expr(1));
        string op = context.GetChild(1).GetText();
        return new BinaryOpNode(op, left, right) { Span = context.Span() };
    }

    public override AstNode VisitIfStat(FishboneParser.IfStatContext context)
    {
        var condition = Visit(context.expr());
        var thenBranch = VisitBody(context.statement(0));

        // an "else if" is just an else whose statement is another ifStat, so chains
        // arrive here pre-nested; keep the resulting IfNode unwrapped
        AstNode? elseBranch = null;
        if (context.statement().Length > 1)
        {
            var elseNode = Visit(context.statement(1));
            elseBranch = elseNode is BlockNode or IfNode ? elseNode : WrapInBlock(elseNode);
        }

        return new IfNode(condition, thenBranch, elseBranch) { Span = context.Span() };
    }

    public override AstNode VisitWhileStat(FishboneParser.WhileStatContext context)
    {
        var condition = Visit(context.expr());
        var body = VisitBody(context.statement());
        return new WhileNode(condition, body) { Span = context.Span() };
    }

    public override AstNode VisitForeachStat(FishboneParser.ForeachStatContext context)
    {
        var iteratorName = context.ID().GetText();
        var iterable = Visit(context.expr());
        var body = VisitBody(context.statement());
        return new ForeachNode(iteratorName, iterable, body) { Span = context.Span() };
    }

    public override AstNode VisitForStat(FishboneParser.ForStatContext context)
    {
        var iteratorName = context.ID().GetText();
        var start = Visit(context.expr(0));
        var end = Visit(context.expr(1));
        var step = (context.expr().Length > 2) ? Visit(context.expr(2)) : null;
        var body = VisitBody(context.statement());
        return new ForNode(iteratorName, start, end, step, body)
        { Span = context.Span() };
    }

    // single-statement bodies get wrapped in a block so they scope and execute
    // exactly like their braced equivalent
    private AstNode VisitBody(FishboneParser.StatementContext context)
    {
        var node = Visit(context);
        return node is BlockNode ? node : WrapInBlock(node);
    }

    private static BlockNode WrapInBlock(AstNode statement) =>
        new BlockNode([statement]) { Span = statement.Span };

    public override AstNode VisitIdExpr(FishboneParser.IdExprContext context)
    {
        return new IdentifierNode(context.ID().GetText()) { Span = context.Span() };
    }

    public override AstNode VisitCastExpr(FishboneParser.CastExprContext context)
    {
        var value = Visit(context.expr());
        var typeName = context.ID().GetText();
        return new CastNode(value, typeName) { Span = context.Span() };
    }

    public override AstNode VisitIntExpr(FishboneParser.IntExprContext context)
    {
        var text = context.INT().GetText().Replace("_", string.Empty);
        var line = context.Start.Line;
        var column = context.Start.Column + 1;
        var span = context.Span();

        // like C#, an integer literal is the smallest type that fits: int, then long
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return new LiteralNode(intValue) { Span = span };
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return new LiteralNode(longValue) { Span = span };

        throw new FishboneParseException([new ParseError(line, column,
            $"Integer literal '{context.INT().GetText()}' is too large for a 64-bit integer.", context.INT().GetText()) { Span = span }]);
    }

    public override AstNode VisitDoubleExpr(FishboneParser.DoubleExprContext context)
    {
        var text = context.DOUBLE().GetText();
        var line = context.Start.Line;
        var column = context.Start.Column + 1;
        var span = context.Span();

        // NumberStyles.Float allows the decimal point and the exponent. an out-of-range
        // literal may either fail to parse or come back as infinity depending on the
        // runtime, so both outcomes are rejected to keep the behavior deterministic
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || double.IsInfinity(value) || double.IsNaN(value))
        {
            throw new FishboneParseException([new ParseError(line, column,
                $"Double literal '{text}' is too large for a 64-bit double.", text) { Span = span }]);
        }

        return new LiteralNode(value) { Span = span };
    }

    public override AstNode VisitStringExpr(FishboneParser.StringExprContext context)
    {
        var text = context.STRING().GetText();
        var trimmed = text[1..^1];
        string unescaped = Unescape(trimmed, context.Start.Line, context.Start.Column + 1);
        return new LiteralNode(unescaped) { Span = context.Span() };
    }

    public override AstNode VisitRawStringExpr(FishboneParser.RawStringExprContext context)
    {
        var text = context.RAW_STRING().GetText();
        var content = text[2..^1].Replace("\"\"", "\"");
        return new LiteralNode(content) { Span = context.Span() };
    }

    public override AstNode VisitInterpStringExpr(FishboneParser.InterpStringExprContext context)
    {
        var token = context.Start;
        var text = context.INTERP_STRING().GetText();
        var inner = text[2..^1]; // between $" and the closing quote

        var parts = new List<AstNode>();
        var literal = new StringBuilder();
        int curLine = token.Line;
        int curCol = token.Column + 1 + 2; // first char after $"
        int litLine = curLine, litCol = curCol;

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;
            var unescaped = Unescape(literal.ToString(), litLine, litCol);
            parts.Add(new LiteralNode(unescaped) { Span = new SourceSpan(litLine, litCol) });
            literal.Clear();
        }

        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '\\')
            {
                // keep the pair raw; Unescape processes it when the run is flushed
                literal.Append(c).Append(inner[i + 1]);
                i++; curCol += 2;
                continue;
            }
            if (c == '{' && i + 1 < inner.Length && inner[i + 1] == '{')
            {
                literal.Append('{');
                i++; curCol += 2;
                continue;
            }
            if (c == '}')
            {
                // the lexer only allows }} outside a hole
                literal.Append('}');
                i++; curCol += 2;
                continue;
            }
            if (c != '{')
            {
                literal.Append(c);
                curCol++;
                continue;
            }

            // start of a {expr} hole
            FlushLiteral();
            curCol++; // past '{'
            int holeStart = i + 1;
            int holeLine = curLine, holeCol = curCol;
            int depth = 1;
            int j = holeStart;
            while (j < inner.Length)
            {
                char h = inner[j];
                if (h == '"')
                {
                    j = SkipQuoted(inner, j, ref curLine, ref curCol);
                    continue;
                }
                if (h == '{') depth++;
                else if (h == '}' && --depth == 0) break;
                Advance(h, ref curLine, ref curCol);
                j++;
            }
            parts.Add(ParseHole(inner[holeStart..j], holeLine, holeCol));
            curCol++; // past '}'
            i = j;
            litLine = curLine; litCol = curCol;
        }
        FlushLiteral();

        return new InterpolatedStringNode([.. parts]) { Span = token.Span() };
    }

    private static AstNode ParseHole(string holeText, int line, int column)
    {
        if (string.IsNullOrWhiteSpace(holeText))
            throw new FishboneParseException([new ParseError(line, column, "Empty interpolation hole in interpolated string.", null)]);

        // the sub-parse is told where the fragment sits, so its spans land in the original script
        // without the fragment having to be padded out to that position first. padding meant
        // re-lexing every one of those newlines and spaces per hole, so a hole's cost grew with
        // how far down the file it was
        return ASTParser.ParseExpression(holeText, line, column);
    }

    // skips a quoted string inside a hole (index is at the opening quote); returns the
    // index just past the closing quote. Backslash pairs are skipped so an escaped quote
    // doesn't terminate the string early, keeping braces inside quotes out of depth counting.
    private static int SkipQuoted(string text, int index, ref int line, ref int col)
    {
        Advance(text[index], ref line, ref col);
        index++;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '\\' && index + 1 < text.Length)
            {
                Advance(c, ref line, ref col);
                Advance(text[index + 1], ref line, ref col);
                index += 2;
                continue;
            }
            Advance(c, ref line, ref col);
            index++;
            if (c == '"')
                break;
        }
        return index;
    }

    private static void Advance(char c, ref int line, ref int col)
    {
        if (c == '\n') { line++; col = 1; }
        else col++;
    }

    private static string Unescape(string text, int line, int column)
    {
        if (!text.Contains('\\'))
            return text;

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            // the lexer only emits a backslash with a character after it
            char escape = text[++i];
            switch (escape)
            {
                case '"': builder.Append('"'); break;
                case '\'': builder.Append('\''); break;
                case '\\': builder.Append('\\'); break;
                case '0': builder.Append('\0'); break;
                case 'a': builder.Append('\a'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'v': builder.Append('\v'); break;
                case 'u':
                    if (i + 4 >= text.Length
                        || !ushort.TryParse(text.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
                        throw InvalidEscape(@"\u requires exactly four hexadecimal digits", line, column);
                    builder.Append((char)codePoint);
                    i += 4;
                    break;
                default:
                    throw InvalidEscape($@"unrecognized escape sequence '\{escape}'", line, column);
            }
        }
        return builder.ToString();
    }

    private static FishboneParseException InvalidEscape(string reason, int line, int column) =>
        new([new ParseError(line, column, $"Invalid string literal: {reason}.", null)]);

    public override AstNode VisitBoolExpr(FishboneParser.BoolExprContext context)
    {
        return (context.TRUE() is not null) ? new LiteralNode(true) { Span = context.Span() } : new LiteralNode(false) { Span = context.Span() };
    }

    public override AstNode VisitNullExpr(FishboneParser.NullExprContext context)
    {
        return new LiteralNode(null!) { Span = context.Span() };
    }
}