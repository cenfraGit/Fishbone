// --------------------------------------------------------------------------------
// Nodes.cs
//
// contains all nodes used in a Fishbone AST.
//
// all nodes are records so that their comparison can be
// straightforward, but some require explicit Equals/GetHashCode
// overrides (mostly nodes consisting of reference types like List<T>
// or IReadOnlyList<T>, etc, because those are reference eqs, not
// element wise)
// --------------------------------------------------------------------------------

using System.Collections.Immutable;

namespace Fishbone.Ast;

// --------------------------------------------------------------------------------
// base
// --------------------------------------------------------------------------------

// base node for all fishbone nodes.
public abstract record AstNode
{
    // the values start at 1 (where 1 is the first row/col in the
    // source file). zero means unknown
    public int Line { get; init; }
    public int Column { get; init; }

    public virtual bool Equals(AstNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return other.GetType() == GetType();
    }

    public override int GetHashCode() => GetType().GetHashCode();
}

// --------------------------------------------------------------------------------
// literals
// --------------------------------------------------------------------------------

public record LiteralNode(object Value) : AstNode;
public record IdentifierNode(string Name) : AstNode;
public record UnaryOpNode(string Operator, AstNode Right) : AstNode;
public record BinaryOpNode(string Operator, AstNode Left, AstNode Right) : AstNode;
public record CastNode(AstNode Value, string TypeName) : AstNode;

// --------------------------------------------------------------------------------
// compound
// --------------------------------------------------------------------------------

public record ListNode(ImmutableArray<AstNode> Elements) : AstNode
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

public record InterpolatedStringNode(ImmutableArray<AstNode> Parts) : AstNode
{
    public virtual bool Equals(InterpolatedStringNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Parts.SequenceEqual(other.Parts);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var part in Parts)
            hash.Add(part);
        return hash.ToHashCode();
    }
}

public record KeyValuePairNode(AstNode Key, AstNode Value) : AstNode;

public record DictionaryNode(ImmutableArray<KeyValuePairNode> Pairs) : AstNode
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

public record IndexingNode(AstNode Target, AstNode Index) : AstNode
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

public record MemberAccessNode(AstNode Target,
                               string MemberName) : AstNode
{
    public virtual bool Equals(MemberAccessNode? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Target.Equals(other.Target) && MemberName == other.MemberName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Target, MemberName);
    }
}

// --------------------------------------------------------------------------------
// calls and args
// --------------------------------------------------------------------------------

// how a call-site argument is passed
public enum ArgumentModifier
{
    None, // by value
    Out,
    Ref
}

// single call argument + argument modifier. not a node itself, just wraps the arg expression.
public record ArgumentNode(ArgumentModifier Modifier, AstNode Value)
{
    public static implicit operator ArgumentNode(AstNode value) => new(ArgumentModifier.None, value);
}

public record CallNode(AstNode Callee, ImmutableArray<ArgumentNode> Arguments) : AstNode
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

// --------------------------------------------------------------------------------
// control flow
// --------------------------------------------------------------------------------

public record IfNode(AstNode Condition,
                     AstNode ThenBranch,
                     AstNode? ElseBranch) : AstNode;

public record WhileNode(AstNode Condition, AstNode Body) : AstNode;

public record ForeachNode(string IteratorName,
                          AstNode Iterable,
                          AstNode Body) : AstNode;

public record ForNode(string IteratorName,
                      AstNode Start,
                      AstNode End,
                      AstNode? Step,
                      AstNode Body) : AstNode;

public record ReturnNode(AstNode? ReturnValue) : AstNode
{
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ReturnValue);
        return hash.ToHashCode();
    }
}
public record BreakNode() : AstNode;
public record ContinueNode() : AstNode;

public record TryNode(BlockNode TryBlock,
                      string? ExceptionName,
                      BlockNode? CatchBlock,
                      BlockNode? FinallyBlock) : AstNode;

public record ThrowNode(AstNode? Value) : AstNode;

// --------------------------------------------------------------------------------
// structure
// --------------------------------------------------------------------------------

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

// --------------------------------------------------------------------------------
// declarations and assignment
// --------------------------------------------------------------------------------

public record DeclarationNode(string Name, AstNode Value) : AstNode
{
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);
        return hash.ToHashCode();
    }
}

public record AssignmentNode(string Name,
                             AstNode Value) : AstNode
{
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Value);
        return hash.ToHashCode();
    }
}

public record IndexedAssignmentNode(AstNode Target,
                                    AstNode Index,
                                    AstNode Value) : AstNode;

public record FunctionDefinitionNode(string Name,
                                     ImmutableArray<string> Parameters,
                                     BlockNode Body) : AstNode
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