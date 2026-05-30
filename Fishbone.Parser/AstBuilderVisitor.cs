using Fishbone.Core;
using Antlr4.Runtime;

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

    public override AstNode VisitStatement(FishboneParser.StatementContext context)
    {
        return Visit(context.GetChild(0));
    }

    public override AstNode VisitDeclarationStat(FishboneParser.DeclarationStatContext context)
    {
        string varName = context.ID().GetText();
        AstNode varValue = Visit(context.expr());
        return new DeclarationNode(varName, varValue);
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
        AstNode? elseBranch = null;

        // todo: handle chained else ifs
        if (context.blockStat().Length > 1)
            elseBranch = Visit(context.blockStat(context.blockStat().Length - 1)); // last block (else)

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
        return new LiteralNode(context.STRING().GetText());
    }

    public override AstNode VisitBoolExpr(FishboneParser.BoolExprContext context)
    {
        return (context.TRUE() is not null) ? new LiteralNode(true) : new LiteralNode(false);
    }
}