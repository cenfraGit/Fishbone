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
            ProgramNode programNode => EvaluateProgram(env, programNode),
            LiteralNode literal => literal.Value,
            IdentifierNode identifier => env.GetValue(identifier.Name),
            DeclarationNode declaration => EvaluateDeclaration(env, declaration),
            AssignmentNode assignment => EvaluateAssignment(env, assignment),
            UnaryOpNode unary => EvaluateUnary(env, unary),
            BinaryOpNode binary => EvaluateBinary(env, binary),
            IfNode ifNode => EvaluateIf(env, ifNode),
            WhileNode whileNode => EvaluateWhile(env, whileNode),
            BlockNode block => EvaluateBlock(env, block),
            _ => throw new NotImplementedException($"Execution for {node.GetType().Name} not yet implemented.")
        };
    }

    public object EvaluateProgram(FishboneEnvironment env, ProgramNode node)
    {
        object lastValue = null!;
        foreach (var statement in node.Statements)
            lastValue = Evaluate(env, statement);
        return lastValue;
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

    public object EvaluateUnary(FishboneEnvironment env, UnaryOpNode node)
    {
        dynamic right = Evaluate(env, node.Right);

        return node.Operator switch
        {
            "-" => -right,
            "not" => !IsTruthy(right),
            _ => throw new Exception($"Unknown unary operator: {node.Operator}")
        };
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
            // comparison
            "==" => left == right,
            "!=" => left != right,
            "<"  => left < right,
            ">"  => left > right,
            "<=" => left <= right,
            ">=" => left >= right,
            _ => throw new Exception($"Unknown binary operator: {node.Operator}")
        };
    }

    public object EvaluateIf(FishboneEnvironment env, IfNode node)
    {
        if (IsTruthy(Evaluate(env, node.Condition)))
            return Evaluate(env, node.ThenBranch);
        else if (node.ElseBranch != null)
            return Evaluate(env, node.ElseBranch);

        return null!;
    }

    public object EvaluateWhile(FishboneEnvironment env, WhileNode node)
    {
        object lastValue = null!;

        while (IsTruthy(Evaluate(env, node.Condition)))
            lastValue = Evaluate(env, node.Body);

        return lastValue;
    }

    public object EvaluateBlock(FishboneEnvironment env, BlockNode node)
    {
        var blockEnv = new FishboneEnvironment(env);

        object lastValue = null!;

        foreach (var statement in node.Statements)
            lastValue = Evaluate(blockEnv, statement);

        return lastValue;
    }

    private bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        double d => d != 0.0,
        string s => !string.IsNullOrEmpty(s),
        _ => true
    };
}