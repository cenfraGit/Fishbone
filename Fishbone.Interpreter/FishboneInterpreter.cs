using Fishbone.Core;

namespace Fishbone.Interpreter;

public class FishboneInterpreter
{
    public object Evaluate(FishboneEnvironment env, AstNode node)
    {
        if (node is null)
            throw new ArgumentNullException(nameof(node));

        return node switch
        {
            LiteralNode literal => literal.Value,
            IdentifierNode identifier => env.GetValue(identifier.Name),
            DeclarationNode declaration => EvaluateDeclaration(env, declaration),
            AssignmentNode assignment => EvaluateAssignment(env, assignment),
            BinaryOpNode binary => EvaluateBinary(env, binary),
            BlockNode block => EvaluateBlock(env, block),
            _ => throw new NotImplementedException($"Execution for {node.GetType().Name} not yet implemented.")
        };
    }

    public object EvaluateDeclaration(FishboneEnvironment env, DeclarationNode node)
    {
        object value = Evaluate(env, node.Value);
        env.Declare(node.Name, value);
        return value;
    }

    public object EvaluateAssignment(FishboneEnvironment env, AssignmentNode node)
    {
        object value = Evaluate(env, node.Value);

        // todo: multiple assignments
        foreach (var name in node.Names)
            env.Assign(name, value);

        return value;
    }

    public object EvaluateBinary(FishboneEnvironment env, BinaryOpNode node)
    {
        dynamic left = Evaluate(env, node.Left);
        dynamic right = Evaluate(env, node.Right);

        return node.Operator switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => left / right,
            _ => throw new Exception($"Unknown operator: {node.Operator}")
        };
    }

    public object EvaluateBlock(FishboneEnvironment env, BlockNode node)
    {
        object lastValue = null!;

        foreach (var statement in node.Statements)
            lastValue = Evaluate(env, statement);

        return lastValue;
    }
}