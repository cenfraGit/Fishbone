using Antlr4.Runtime.Misc;
using Fishbone.Core;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Fishbone.Parser;

public class AstBuilderVisitor : FishboneBaseVisitor<AstNode>
{
    public override AstNode VisitProgram(FishboneParser.ProgramContext context)
    {
        var statements = new List<AstNode>();

        foreach (var statement in context.statement())
            statements.Add(Visit(statement));

        return new ProgramNode(statements) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitBlockStat(FishboneParser.BlockStatContext context)
    {
        var statements = new List<AstNode>();

        foreach (var statement in context.statement())
            statements.Add(Visit(statement));

        return new BlockNode(statements) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitFunctionDefinitionStat(FishboneParser.FunctionDefinitionStatContext context)
    {
        var funcName = context.ID(0).GetText();
        var block = Visit(context.blockStat());

        // get parameters
        var funcParams = new List<string>();
        for (int i = 1; i < context.ID().Length; i++)
            funcParams.Add(context.ID(i).GetText());

        return new FunctionDefinitionNode(funcName, funcParams.ToImmutableArray(), (BlockNode)block) { Line = context.Start.Line, Column = context.Start.Column + 1 };
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

        return new CallNode(callee, funcArgs.ToImmutableArray()) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitReturnStat(FishboneParser.ReturnStatContext context)
    {
        var values = new List<AstNode>();
        for (int i = 0; i < context.expr().Length; i++)
            values.Add(Visit(context.expr(i)));
        return new ReturnNode(values) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitBreakStat(FishboneParser.BreakStatContext context)
    {
        return new BreakNode() { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitContinueStat(FishboneParser.ContinueStatContext context)
    {
        return new ContinueNode() { Line = context.Start.Line, Column = context.Start.Column + 1 };
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

        return new ListNode(elements.ToImmutableArray()) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitDictionaryExpr(FishboneParser.DictionaryExprContext context)
    {
        var keyValuePairs = new List<KeyValuePairNode>();
        for (int i = 0; i < context.dictPair().Count(); i++)
        {
            var dictPair = context.dictPair(i);
            var key = Visit(dictPair.expr(0));
            var value = Visit(dictPair.expr(1));
            keyValuePairs.Add(new KeyValuePairNode(key, value) { Line = dictPair.Start.Line, Column = dictPair.Start.Column + 1 });
        }
        return new DictionaryNode(keyValuePairs.ToImmutableArray()) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitIndexingExpr(FishboneParser.IndexingExprContext context)
    {
        var target = Visit(context.expr(0));
        var index = Visit(context.expr(1));
        return new IndexingNode(target, index) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitMemberAccessExpr(FishboneParser.MemberAccessExprContext context)
    {
        var target = Visit(context.expr());
        var id = context.ID().GetText();
        return new MemberAccessNode(target, id) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitDeclarationStat(FishboneParser.DeclarationStatContext context)
    {
        var names = context.ID().Select(id => id.GetText()).ToList();
        AstNode value = Visit(context.expr());
        return new DeclarationNode(names, value) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitAssignmentStat(FishboneParser.AssignmentStatContext context)
    {
        var names = context.ID().Select(id => id.GetText()).ToList();
        AstNode value = Visit(context.expr());
        return new AssignmentNode(names, value) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitIndexedAssignmentStat(FishboneParser.IndexedAssignmentStatContext context)
    {
        AstNode assignmentTarget = Visit(context.expr(0));
        if (assignmentTarget is not IndexingNode indexingNode)
            throw new InvalidOperationException($"Indexed assignment requires an indexed target, but found {assignmentTarget.GetType().Name}.");

        AstNode value = Visit(context.expr(1));
        return new IndexedAssignmentNode(indexingNode.Target, indexingNode.Index, value) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitCompoundAssignmentStat(FishboneParser.CompoundAssignmentStatContext context)
    {
        AstNode target = Visit(context.expr(0));
        AstNode rightValue = Visit(context.expr(1));

        // the compound operator text is "+=" (for example), the underlying binary operator is "+"
        string compoundOp = context.GetChild(1).GetText();
        string binaryOp = compoundOp[..^1];

        var line = context.Start.Line;
        var column = context.Start.Column + 1;

        // "target <op>= right" converts to "target = target <op> right".
        // plan: combine assignment node with operator node
        switch (target)
        {
            case IdentifierNode identifier:
                var combinedValue = new BinaryOpNode(binaryOp, identifier, rightValue) { Line = line, Column = column };
                return new AssignmentNode([identifier.Name], combinedValue) { Line = line, Column = column };

            case IndexingNode indexing:
                var combinedIndexedValue = new BinaryOpNode(binaryOp, indexing, rightValue) { Line = line, Column = column };
                return new IndexedAssignmentNode(indexing.Target, indexing.Index, combinedIndexedValue) { Line = line, Column = column };

            default:
                throw new InvalidOperationException($"Compound assignment requires a variable or indexed target, but found {target.GetType().Name}.");
        }
    }

    public override AstNode VisitUnaryExpr(FishboneParser.UnaryExprContext context)
    {
        string op = context.GetChild(0).GetText();
        AstNode right = Visit(context.expr());
        return new UnaryOpNode(op, right) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitBinaryExpr(FishboneParser.BinaryExprContext context)
    {
        AstNode left = Visit(context.expr(0));
        AstNode right = Visit(context.expr(1));
        string op = context.GetChild(1).GetText();
        return new BinaryOpNode(op, left, right) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitBoolOperatorExpr(FishboneParser.BoolOperatorExprContext context)
    {
        AstNode left = Visit(context.expr(0));
        AstNode right = Visit(context.expr(1));
        string op = context.GetChild(1).GetText();
        return new BinaryOpNode(op, left, right) { Line = context.Start.Line, Column = context.Start.Column + 1 };
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

        return new IfNode(condition, thenBranch, elseBranch) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitWhileStat(FishboneParser.WhileStatContext context)
    {
        var condition = Visit(context.expr());
        var body = VisitBody(context.statement());
        return new WhileNode(condition, body) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitForeachStat(FishboneParser.ForeachStatContext context)
    {
        var iteratorName = context.ID().GetText();
        var iterable = Visit(context.expr());
        var body = VisitBody(context.statement());
        return new ForeachNode(iteratorName, iterable, body) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitForStat(FishboneParser.ForStatContext context)
    {
        var iteratorName = context.ID().GetText();
        var start = Visit(context.expr(0));
        var end = Visit(context.expr(1));
        var step = (context.expr().Length > 2) ? Visit(context.expr(2)) : null;
        var body = VisitBody(context.statement());
        return new ForNode(iteratorName, start, end, step, body)
        { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    // single-statement bodies get wrapped in a block so they scope and execute
    // exactly like their braced equivalent
    private AstNode VisitBody(FishboneParser.StatementContext context)
    {
        var node = Visit(context);
        return node is BlockNode ? node : WrapInBlock(node);
    }

    private static BlockNode WrapInBlock(AstNode statement) =>
        new BlockNode([statement]) { Line = statement.Line, Column = statement.Column };

    public override AstNode VisitIdExpr(FishboneParser.IdExprContext context)
    {
        return new IdentifierNode(context.ID().GetText()) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitCastExpr(FishboneParser.CastExprContext context)
    {
        var value = Visit(context.expr());
        var typeName = context.ID().GetText();
        return new CastNode(value, typeName) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitIntExpr(FishboneParser.IntExprContext context)
    {
        var text = context.INT().GetText();
        text = text.Replace("_", string.Empty);
        return new LiteralNode(int.Parse(text, CultureInfo.InvariantCulture)) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitDoubleExpr(FishboneParser.DoubleExprContext context)
    {
        return new LiteralNode(double.Parse(context.DOUBLE().GetText(), CultureInfo.InvariantCulture)) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitStringExpr(FishboneParser.StringExprContext context)
    {
        var text = context.STRING().GetText();
        var trimmed = text[1..^1];
        string unescaped = Unescape(trimmed, context.Start.Line, context.Start.Column + 1);
        return new LiteralNode(unescaped) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitRawStringExpr(FishboneParser.RawStringExprContext context)
    {
        var text = context.RAW_STRING().GetText();
        var content = text[2..^1].Replace("\"\"", "\"");
        return new LiteralNode(content) { Line = context.Start.Line, Column = context.Start.Column + 1 };
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
            parts.Add(new LiteralNode(unescaped) { Line = litLine, Column = litCol });
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

        return new InterpolatedStringNode([.. parts]) { Line = token.Line, Column = token.Column + 1 };
    }

    private static AstNode ParseHole(string holeText, int line, int column)
    {
        if (string.IsNullOrWhiteSpace(holeText))
            throw new FishboneParseException([new ParseError(line, column, "Empty interpolation hole in interpolated string.", null)]);

        // pad the fragment so the sub-parsed expression reports positions in the original script
        var padded = new string('\n', line - 1) + new string(' ', column - 1) + holeText;
        return ASTParser.ParseExpression(padded);
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
        return (context.TRUE() is not null) ? new LiteralNode(true) { Line = context.Start.Line, Column = context.Start.Column + 1 } : new LiteralNode(false) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitNullExpr(FishboneParser.NullExprContext context)
    {
        return new LiteralNode(null!) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }
}