namespace Fishbone.Core;

public abstract record AstNode;

public record DeclarationNode(string Name, AstNode Value) : AstNode;
public record UnaryOpNode(string Operator, AstNode Right) : AstNode;
public record BinaryOpNode(string Operator, AstNode Left, AstNode Right) : AstNode;
public record IfNode(AstNode Condition, AstNode ThenBranch, AstNode? ElseBranch) : AstNode;
public record WhileNode(AstNode Condition, AstNode Body) : AstNode;
public record IdentifierNode(string Name) : AstNode;
public record LiteralNode(object Value) : AstNode;


public record ReturnNode(List<AstNode> ReturnValues) : AstNode;
public record BreakNode() : AstNode;
public record ContinueNode() : AstNode;

public record ProgramNode(IReadOnlyList<AstNode> Statements) : AstNode
{
    public virtual bool Equals(ProgramNode? other)
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

public record DeclarationNode(IReadOnlyList<string> Names, AstNode Value) : AstNode
{
    public virtual bool Equals(DeclarationNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Names.SequenceEqual(other.Names) && Equals(Value, other.Value);
}

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in Names)
        {
            hash.Add(name);
        }
        hash.Add(Value);
        return hash.ToHashCode();
    }
}

public record AssignmentNode(IReadOnlyList<string> Names, AstNode Value) : AstNode
{
    public virtual bool Equals(AssignmentNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Names.SequenceEqual(other.Names) && Equals(Value, other.Value);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in Names)
        {
            hash.Add(name);
        }
        hash.Add(Value);
        return hash.ToHashCode();
    }
}