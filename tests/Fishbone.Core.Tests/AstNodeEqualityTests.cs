namespace Fishbone.Core.Tests;

public class AstNodeEqualityTests
{
    [Fact]
    public void ProgramNode_UsesStructuralStatementEquality()
    {
        var left = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["x"], new LiteralNode(1))
        });
        var right = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["x"], new LiteralNode(1))
        });

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void BlockNode_UsesStructuralStatementEquality()
    {
        var left = new BlockNode(new List<AstNode>
        {
            new AssignmentNode(["x"], new LiteralNode(2))
        });
        var right = new BlockNode(new List<AstNode>
        {
            new AssignmentNode(["x"], new LiteralNode(2))
        });

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void DeclarationNode_UsesStructuralNameEquality()
    {
        var left = new DeclarationNode(["x", "y"], new IdentifierNode("source"));
        var right = new DeclarationNode(["x", "y"], new IdentifierNode("source"));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void AssignmentNode_UsesStructuralNameEquality()
    {
        var left = new AssignmentNode(["x", "y"], new IdentifierNode("source"));
        var right = new AssignmentNode(["x", "y"], new IdentifierNode("source"));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void IndexedAssignmentNode_UsesStructuralEquality()
    {
        var left = new IndexedAssignmentNode(
            new IdentifierNode("values"),
            new LiteralNode(1),
            new LiteralNode(42));
        var right = new IndexedAssignmentNode(
            new IdentifierNode("values"),
            new LiteralNode(1),
            new LiteralNode(42));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ReturnNode_UsesStructuralValueEquality()
    {
        var left = new ReturnNode([new IdentifierNode("x"), new LiteralNode(1)]);
        var right = new ReturnNode([new IdentifierNode("x"), new LiteralNode(1)]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void CallNode_UsesStructuralCalleeAndArgumentEquality()
    {
        var left = new CallNode(new IdentifierNode("record"), [new LiteralNode(1), new IdentifierNode("value")]);
        var right = new CallNode(new IdentifierNode("record"), [new LiteralNode(1), new IdentifierNode("value")]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void FunctionDefinitionNode_UsesStructuralParameterAndBodyEquality()
    {
        var left = new FunctionDefinitionNode(
            "add",
            ["left", "right"],
            new BlockNode(new List<AstNode>
            {
                new ReturnNode([
                    new BinaryOpNode("+", new IdentifierNode("left"), new IdentifierNode("right"))
                ])
            })
        );
        var right = new FunctionDefinitionNode(
            "add",
            ["left", "right"],
            new BlockNode(new List<AstNode>
            {
                new ReturnNode([
                    new BinaryOpNode("+", new IdentifierNode("left"), new IdentifierNode("right"))
                ])
            })
        );

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ListNode_UsesStructuralElementEquality()
    {
        var left = new ListNode([new LiteralNode(1), new IdentifierNode("x")]);
        var right = new ListNode([new LiteralNode(1), new IdentifierNode("x")]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void DictionaryNode_UsesStructuralPairEquality()
    {
        var left = new DictionaryNode([
            new KeyValuePairNode(new LiteralNode("x"), new LiteralNode(1)),
            new KeyValuePairNode(new IdentifierNode("key"), new IdentifierNode("value"))
        ]);
        var right = new DictionaryNode([
            new KeyValuePairNode(new LiteralNode("x"), new LiteralNode(1)),
            new KeyValuePairNode(new IdentifierNode("key"), new IdentifierNode("value"))
        ]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void IndexingNode_UsesStructuralTargetAndIndexEquality()
    {
        var left = new IndexingNode(
            new IdentifierNode("values"),
            new BinaryOpNode("+", new IdentifierNode("i"), new LiteralNode(1))
        );
        var right = new IndexingNode(
            new IdentifierNode("values"),
            new BinaryOpNode("+", new IdentifierNode("i"), new LiteralNode(1))
        );

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void MemberAccessNode_UsesStructuralTargetAndMemberEquality()
    {
        var left = new MemberAccessNode(new IdentifierNode("target"), "Name");
        var right = new MemberAccessNode(new IdentifierNode("target"), "Name");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void ListBackedNodes_WhenOrderDiffers_AreNotEqual()
    {
        var left = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["x"], new LiteralNode(1)),
            new DeclarationNode(["y"], new LiteralNode(2))
        });
        var right = new ProgramNode(new List<AstNode>
        {
            new DeclarationNode(["y"], new LiteralNode(2)),
            new DeclarationNode(["x"], new LiteralNode(1))
        });

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void ListNode_WhenOrderDiffers_IsNotEqual()
    {
        var left = new ListNode([new LiteralNode(1), new LiteralNode(2)]);
        var right = new ListNode([new LiteralNode(2), new LiteralNode(1)]);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void DictionaryNode_WhenPairOrderDiffers_IsNotEqual()
    {
        var left = new DictionaryNode([
            new KeyValuePairNode(new LiteralNode("x"), new LiteralNode(1)),
            new KeyValuePairNode(new LiteralNode("y"), new LiteralNode(2))
        ]);
        var right = new DictionaryNode([
            new KeyValuePairNode(new LiteralNode("y"), new LiteralNode(2)),
            new KeyValuePairNode(new LiteralNode("x"), new LiteralNode(1))
        ]);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void IndexingNode_WhenIndexDiffers_IsNotEqual()
    {
        var left = new IndexingNode(new IdentifierNode("values"), new LiteralNode(0));
        var right = new IndexingNode(new IdentifierNode("values"), new LiteralNode(1));

        Assert.NotEqual(left, right);
    }
}