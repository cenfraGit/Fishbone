using System.Collections.Immutable;

namespace Fishbone.Core;

public abstract record AstNode;

public record UnaryOpNode(string Operator, AstNode Right) : AstNode;
public record BinaryOpNode(string Operator, AstNode Left, AstNode Right) : AstNode;
public record IfNode(AstNode Condition, AstNode ThenBranch, AstNode? ElseBranch) : AstNode;
public record WhileNode(AstNode Condition, AstNode Body) : AstNode;
public record ForeachNode(string IteratorName, AstNode Iterable, AstNode Body) : AstNode;
public record IdentifierNode(string Name) : AstNode;
public record LiteralNode(object Value) : AstNode;


public record ReturnNode(IReadOnlyList<AstNode> ReturnValues) : AstNode
{
    public virtual bool Equals(ReturnNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return ReturnValues.SequenceEqual(other.ReturnValues);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in ReturnValues)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}
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

public record FunctionDefinitionNode(
    string Name,
    ImmutableArray<string> Parameters,
    BlockNode Body
) : AstNode
{
    public virtual bool Equals(FunctionDefinitionNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name
            && Parameters.SequenceEqual(other.Parameters)
            && Equals(Body, other.Body);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var parameter in Parameters)
        {
            hash.Add(parameter);
        }
        hash.Add(Body);
        return hash.ToHashCode();
    }
}

public record CallNode(
    AstNode Callee,
    ImmutableArray<AstNode> Arguments
) : AstNode
{
    public virtual bool Equals(CallNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Equals(Callee, other.Callee) && Arguments.SequenceEqual(other.Arguments);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Callee);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }
        return hash.ToHashCode();
    }
}

public record ListNode(
    ImmutableArray<AstNode> Elements
) : AstNode
{
    public virtual bool Equals(ListNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Elements.SequenceEqual(other.Elements);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var element in Elements)
        {
            hash.Add(element);
        }
        return hash.ToHashCode();
    }
}

public record KeyValuePairNode(AstNode Key, AstNode Value) : AstNode;

public record DictionaryNode(
    ImmutableArray<KeyValuePairNode> Pairs
) : AstNode
{
    public virtual bool Equals(DictionaryNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Pairs.SequenceEqual(other.Pairs);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var pair in Pairs)
            hash.Add(pair);
        return hash.ToHashCode();
    }
}

public record IndexingNode(
    AstNode Target,
    AstNode Index
) : AstNode
{
    public virtual bool Equals(IndexingNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Target.Equals(other.Target) && Index.Equals(other.Index);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Target, Index);
    }
}