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

        return new ProgramNode(statements);
    }

    public override AstNode VisitBlockStat(FishboneParser.BlockStatContext context)
    {
        var statements = new List<AstNode>();

        foreach (var statement in context.statement())
            statements.Add(Visit(statement));

        return new BlockNode(statements);
    }

    public override AstNode VisitFunctionDefinitionStat(FishboneParser.FunctionDefinitionStatContext context)
    {
        var funcName = context.ID(0).GetText();
        var block = Visit(context.blockStat());

        // get parameters
        var funcParams = new List<string>();
        for (int i = 1; i < context.ID().Length; i++)
            funcParams.Add(context.ID(i).GetText());

        return new FunctionDefinitionNode(funcName, funcParams.ToImmutableArray(), (BlockNode)block);
    }

    public override AstNode VisitFunctionCallStat(FishboneParser.FunctionCallStatContext context)
    {
        var funcName = context.ID().GetText();
        var funcArgs = new List<AstNode>();

        for (int i = 0; i < context.expr().Length; i++)
            funcArgs.Add(Visit(context.expr(i)));

        return new FunctionCallNode(funcName, funcArgs.ToImmutableArray());
    }

    public override AstNode VisitFunctionCallExpr(FishboneParser.FunctionCallExprContext context)
    {
        return Visit(context.functionCallStat());
    }

    public override AstNode VisitReturnStat(FishboneParser.ReturnStatContext context)
    {
        var values = new List<AstNode>();
        for (int i = 0; i < context.expr().Length; i++)
            values.Add(Visit(context.expr(i)));
        return new ReturnNode(values);
    }

    public override AstNode VisitBreakStat(FishboneParser.BreakStatContext context)
    {
        return new BreakNode();
    }

    public override AstNode VisitContinueStat(FishboneParser.ContinueStatContext context)
    {
        return new ContinueNode();
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

    public override AstNode VisitDeclarationStat(FishboneParser.DeclarationStatContext context)
    {
        var names = context.ID().Select(id => id.GetText()).ToList();
        AstNode value = Visit(context.expr());
        return new DeclarationNode(names, value);
    }

    public override AstNode VisitAssignmentStat(FishboneParser.AssignmentStatContext context)
    {
        var names = context.ID().Select(id => id.GetText()).ToList();
        AstNode value = Visit(context.expr());
        return new AssignmentNode(names, value);
    }

    public override AstNode VisitUnaryExpr(FishboneParser.UnaryExprContext context)
    {
        string op = context.GetChild(0).GetText();
        AstNode right = Visit(context.expr());
        return new UnaryOpNode(op, right);
    }

    public override AstNode VisitBinaryExpr(FishboneParser.BinaryExprContext context)
    {
        AstNode left = Visit(context.expr(0));
        AstNode right = Visit(context.expr(1));
        string op = context.GetChild(1).GetText();
        return new BinaryOpNode(op, left, right);
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
            elseBranch = new IfNode(elseIfCondition, elseIfBranch, elseBranch);
        }

        return new IfNode(condition, thenBranch, elseBranch);
    }

    public override AstNode VisitWhileStat(FishboneParser.WhileStatContext context)
    {
        var condition = Visit(context.expr());
        var body = Visit(context.blockStat());
        return new WhileNode(condition, body);
    }

    public override AstNode VisitIdExpr(FishboneParser.IdExprContext context)
    {
        return new IdentifierNode(context.ID().GetText());
    }

    public override AstNode VisitIntExpr(FishboneParser.IntExprContext context)
    {
        return new LiteralNode(int.Parse(context.INT().GetText()));
    }

    public override AstNode VisitFloatExpr(FishboneParser.FloatExprContext context)
    {
        return new LiteralNode(double.Parse(context.FLOAT().GetText()));
    }

    public override AstNode VisitStringExpr(FishboneParser.StringExprContext context)
    {
        var text = context.STRING().GetText();
        var trimmed = text[1..^1];
        string unescaped = Regex.Unescape(trimmed);
        return new LiteralNode(unescaped);
    }

    public override AstNode VisitBoolExpr(FishboneParser.BoolExprContext context)
    {
        return (context.TRUE() is not null) ? new LiteralNode(true) : new LiteralNode(false);
    }
}
