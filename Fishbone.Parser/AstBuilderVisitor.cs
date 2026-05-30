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

        return new BlockNode(statements);
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

    public override AstNode VisitMDASExpr(FishboneParser.MDASExprContext context)
    {
        AstNode left = Visit(context.expr(0));
        AstNode right = Visit(context.expr(1));
        string op = context.GetChild(1).GetText();
        return new BinaryOpNode(op, left, right);
    }

    public override AstNode VisitIdExpr(FishboneParser.IdExprContext context)
    {
        return new IdentifierNode(context.ID().GetText());
    }

    public override AstNode VisitIntExpr(FishboneParser.IntExprContext context)
    {
        return new LiteralNode(int.Parse(context.INT().GetText()));
    }
}