using Antlr4.Runtime.Misc;
using Fishbone.Core;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

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
        var callee = Visit(context.expr(0));
        var funcArgs = new List<AstNode>();

        for (int i = 1; i < context.expr().Length; i++)
            funcArgs.Add(Visit(context.expr(i)));

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
        var condition = Visit(context.expr(0));
        var thenBranch = Visit(context.blockStat(0)); // first block

        var elseIfCount = context.ELSEIF().Length;
        var hasElse = context.ELSE() is not null;
        AstNode? elseBranch = hasElse
            ? Visit(context.blockStat(context.blockStat().Length - 1))
            : null;

        for (int i = elseIfCount - 1; i >= 0; i--)
        {
            var elseIfCondition = Visit(context.expr(i + 1));
            var elseIfBranch = Visit(context.blockStat(i + 1));
            elseBranch = new IfNode(elseIfCondition, elseIfBranch, elseBranch) { Line = context.ELSEIF(i).Symbol.Line, Column = context.ELSEIF(i).Symbol.Column + 1 };
        }

        return new IfNode(condition, thenBranch, elseBranch) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitWhileStat(FishboneParser.WhileStatContext context)
    {
        var condition = Visit(context.expr());
        var body = Visit(context.blockStat());
        return new WhileNode(condition, body) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitForeachStat(FishboneParser.ForeachStatContext context)
    {
        var iteratorName = context.ID().GetText();
        var iterable = Visit(context.expr());
        var body = Visit(context.blockStat());
        return new ForeachNode(iteratorName, iterable, body) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitForStat(FishboneParser.ForStatContext context)
    {
        var iteratorName = context.ID().GetText();
        var start = Visit(context.expr(0));
        var end = Visit(context.expr(1));
        var step = (context.expr().Length > 2) ? Visit(context.expr(2)) : null;
        var body = Visit(context.blockStat());
        return new ForNode(iteratorName, start, end, step, body) 
        { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitIdExpr(FishboneParser.IdExprContext context)
    {
        return new IdentifierNode(context.ID().GetText()) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitIntExpr(FishboneParser.IntExprContext context)
    {
        var text = context.INT().GetText();
        text = text.Replace("_", string.Empty);
        return new LiteralNode(int.Parse(text)) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitDoubleExpr(FishboneParser.DoubleExprContext context)
    {
        return new LiteralNode(double.Parse(context.DOUBLE().GetText())) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitStringExpr(FishboneParser.StringExprContext context)
    {
        var text = context.STRING().GetText();
        var trimmed = text[1..^1];
        string unescaped = Regex.Unescape(trimmed);
        return new LiteralNode(unescaped) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitBoolExpr(FishboneParser.BoolExprContext context)
    {
        return (context.TRUE() is not null) ? new LiteralNode(true) { Line = context.Start.Line, Column = context.Start.Column + 1 } : new LiteralNode(false) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }

    public override AstNode VisitNullExpr(FishboneParser.NullExprContext context)
    {
        return new LiteralNode(null!) { Line = context.Start.Line, Column = context.Start.Column + 1 };
    }
}