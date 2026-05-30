namespace Fishbone.Core;

public abstract record AstNode;

public record DeclarationNode(string Name, AstNode Value) : AstNode;
public record AssignmentNode(IReadOnlyList<string> Names, AstNode Value) : AstNode;
public record UnaryOpNode(string Operator, AstNode Right) : AstNode;
public record BinaryOpNode(string Operator, AstNode Left, AstNode Right) : AstNode;
public record IfNode(AstNode Condition, AstNode ThenBranch, AstNode? ElseBranch) : AstNode;
public record WhileNode(AstNode Condition, AstNode Body) : AstNode;
public record IdentifierNode(string Name) : AstNode;
public record LiteralNode(object Value) : AstNode;

public record ProgramNode(List<AstNode> Statements) : AstNode;

public record BlockNode(IReadOnlyList<AstNode> Statements) : AstNode
{
    public virtual bool Equals(BlockNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Statements.SequenceEqual(other.Statements);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var stmt in Statements)
        {
            hash.Add(stmt);
        }
        return hash.ToHashCode();
    }
}