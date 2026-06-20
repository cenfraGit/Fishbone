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
            FunctionDefinitionNode functionDefinition => EvaluateFunctionDefinition(env, functionDefinition),
            FunctionCallNode functionCall => EvaluateFunctionCall(env, functionCall),
            ListNode listNode => EvaluateListNode(env, listNode),
            ReturnNode returnNode => EvaluateReturn(env, returnNode),
            BreakNode breakNode => EvaluateBreak(env, breakNode),
            ContinueNode continueNode => EvaluateContinue(env, continueNode),
            _ => throw new NotImplementedException($"Execution for {node.GetType().Name} not yet implemented.")
        };
    }

    internal object EvaluateProgram(FishboneEnvironment env, ProgramNode node)
    {
        object lastValue = null!;
        foreach (var statement in node.Statements)
            lastValue = Evaluate(env, statement);
        return lastValue;
    }

    internal object EvaluateDeclaration(FishboneEnvironment env, DeclarationNode node)
    {
        // eval right side
        object rawValue = Evaluate(env, node.Value);

        // if declaring single var, skip deconstruction
        // prevents collections from being unpacked
        if (node.Names.Count == 1)
        {
            env.Declare(node.Names[0], rawValue);
            return rawValue;
        }

        // right side is always handled as list
        List<object> valueList = rawValue is List<object> list
            ? list
            : new List<object> { rawValue };

        // deconstruct and assign
        for (int i = 0; i < node.Names.Count; i++)
        {
            string name = node.Names[i];
            object elementValue = i < valueList.Count ? valueList[i] : null!;

            env.Declare(name, elementValue);
        }

        return rawValue;
    }

    internal object EvaluateAssignment(FishboneEnvironment env, AssignmentNode node)
    {
        // eval right side
        object rawValue = Evaluate(env, node.Value);

        // if declaring single var, skip deconstruction
        // prevents collections from being unpacked
        if (node.Names.Count == 1)
        {
            env.Assign(node.Names[0], rawValue);
            return rawValue;
        }

        // right side is always handled as list
        List<object> valueList = rawValue is List<object> list
            ? list
            : new List<object> { rawValue };

        // deconstruct and assign
        for (int i = 0; i < node.Names.Count; i++)
        {
            string name = node.Names[i];
            object elementValue = i < valueList.Count ? valueList[i] : null!;
            env.Assign(name, elementValue);
        }

        return rawValue;
    }

    internal object EvaluateUnary(FishboneEnvironment env, UnaryOpNode node)
    {
        dynamic right = Evaluate(env, node.Right);

        return node.Operator switch
        {
            "-" => -right,
            "not" => !IsTruthy(right),
            _ => throw new Exception($"Unknown unary operator: {node.Operator}")
        };
    }

    internal object EvaluateBinary(FishboneEnvironment env, BinaryOpNode node)
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

    internal object EvaluateIf(FishboneEnvironment env, IfNode node)
    {
        if (IsTruthy(Evaluate(env, node.Condition)))
            return Evaluate(env, node.ThenBranch);
        else if (node.ElseBranch != null)
            return Evaluate(env, node.ElseBranch);

        return null!;
    }

    internal object EvaluateWhile(FishboneEnvironment env, WhileNode node)
    {
        object lastValue = null!;

        while (IsTruthy(Evaluate(env, node.Condition)))
        {
            try
            {
                lastValue = Evaluate(env, node.Body);
            }
            catch (ContinueException)
            {
                continue;
            }
            catch (BreakException)
            {
                break;
            }
        }

        return lastValue;
    }

    internal object EvaluateBlock(FishboneEnvironment env, BlockNode node)
    {
        var blockEnv = new FishboneEnvironment(env);

        object lastValue = null!;

        foreach (var statement in node.Statements)
            lastValue = Evaluate(blockEnv, statement);

        return lastValue;
    }

    internal object EvaluateFunctionDefinition(FishboneEnvironment env,  FunctionDefinitionNode node)
    {
        var function = new FishboneFunction(node, env);
        env.Declare(node.Name, function);
        return null!;
    }

    internal object EvaluateFunctionCall(FishboneEnvironment env, FunctionCallNode node)
    {
        object callee = env.GetValue(node.Name);

        // eval arguments
        var evaluatedArgs = new List<object>();
        foreach (var argNode in node.Arguments)
            evaluatedArgs.Add(Evaluate(env, argNode));

        // check if c# delegate
        if (callee is Delegate csharpDelegate)
        {
            var methodParameters = csharpDelegate.Method.GetParameters();

            // check arg count
            if (evaluatedArgs.Count != methodParameters.Length)
                throw new Exception($"Expected {methodParameters.Length} args but got {evaluatedArgs.Count}.");

            var marshalledArgs = new object[evaluatedArgs.Count];
            for (int i = 0; i < evaluatedArgs.Count; i++)
            {
                object rawArg = evaluatedArgs[i];
                Type targetType = methodParameters[i].ParameterType;

                if (rawArg == null)
                {
                    marshalledArgs[i] = targetType.IsValueType ? Activator.CreateInstance(targetType)! : null!;
                    continue;
                }

                // convert primitives
                if (targetType.IsPrimitive && rawArg.GetType().IsPrimitive)
                {
                    marshalledArgs[i] = Convert.ChangeType(rawArg, targetType);
                }
                else
                {
                    marshalledArgs[i] = rawArg;
                }
            }

            try
            {
                // execute with new types
                return csharpDelegate.DynamicInvoke(marshalledArgs)!;
            }
            catch (System.Reflection.TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        // check if fishbone callable
        if (callee is ICallable fishboneFunction)
        {
            // check arg count
            if (evaluatedArgs.Count != fishboneFunction.Arity)
                throw new Exception($"Expected {fishboneFunction.Arity} args but got {evaluatedArgs.Count}.");

            // execute
            return fishboneFunction.Call(this, evaluatedArgs);
        }

        throw new Exception($"Symbol \"{node.Name}\" is not a callable target.");
    }

    internal object EvaluateListNode(FishboneEnvironment env, ListNode node)
    {
        return node.Elements.Select(i => Evaluate(env, i)).ToList();
    }

    internal object EvaluateReturn(FishboneEnvironment env, ReturnNode node)
    {
        // return;
        if (node.ReturnValues.Count == 0)
            throw new ReturnException(null!);

        // return expr, expr;
        var returnValues = new List<object>();
        for (int i = 0; i < node.ReturnValues.Count; i++)
            returnValues.Add(Evaluate(env, node.ReturnValues[i]));

        throw new ReturnException(returnValues);
    }

    internal object EvaluateBreak(FishboneEnvironment env, BreakNode node)
    {
        throw new BreakException();
    }

    internal object EvaluateContinue(FishboneEnvironment env, ContinueNode node)
    {
        throw new ContinueException();
    }

    internal bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        double d => d != 0.0,
        string s => !string.IsNullOrEmpty(s),
        _ => true
    };
}