using Fishbone.Core;
using System.Collections;
using System.Reflection;

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
            ForeachNode foreachNode => EvaluateForeach(env, foreachNode),
            BlockNode block => EvaluateBlock(env, block),
            FunctionDefinitionNode functionDefinition => EvaluateFunctionDefinition(env, functionDefinition),
            CallNode callNode => EvaluateCallNode(env, callNode),
            ListNode listNode => EvaluateListNode(env, listNode),
            DictionaryNode dictionaryNode => EvaluateDictionaryNode(env, dictionaryNode),
            IndexingNode indexingNode => EvaluateIndexingNode(env, indexingNode),
            MemberAccessNode memberAccessNode => EvaluateMemberAccessNode(env, memberAccessNode),
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

    internal object EvaluateForeach(FishboneEnvironment env, ForeachNode node)
    {
        var iterable = Evaluate(env, node.Iterable);
        IEnumerable values = iterable switch
        {
            IDictionary dictionary => dictionary.Keys,
            IEnumerable enumerable when enumerable is not string => enumerable,
            _ => throw new Exception("Object is not iterable.")
        };

        var loopEnv = new FishboneEnvironment(env);
        loopEnv.Declare(node.IteratorName, null!);
        object lastValue = null!;

        foreach (var value in values)
        {
            loopEnv.Assign(node.IteratorName, value!);

            try
            {
                lastValue = Evaluate(loopEnv, node.Body);
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

    internal object EvaluateCallNode(FishboneEnvironment env, CallNode node)
    {
        var callee = Evaluate(env, node.Callee);
        return EvaluateCall(env, callee, node.Arguments);
    }

    internal object EvaluateCall(FishboneEnvironment env, object callee, IReadOnlyList<AstNode> argumentNodes)
    {
        if (callee is ICallable fishboneFunction)
        {
            if (argumentNodes.Count != fishboneFunction.Arity)
                throw new Exception($"Expected {fishboneFunction.Arity} args but got {argumentNodes.Count}.");

            var evaluatedArgs = new List<object>();
            foreach (var argNode in argumentNodes)
                evaluatedArgs.Add(Evaluate(env, argNode));

            return fishboneFunction.Call(this, evaluatedArgs);
        }

        if (callee is Delegate csharpDelegate)
            return InvokeReflectedCallable(env, csharpDelegate.Target, csharpDelegate.Method, argumentNodes);

        if (callee is BoundMethod boundMethod)
            return InvokeBoundMethod(env, boundMethod, argumentNodes);

        if (callee is null)
            throw new Exception("Null is not callable.");

        throw new Exception($"Object of type \"{callee.GetType().Name}\" is not callable.");
    }

    internal object InvokeBoundMethod(FishboneEnvironment env, BoundMethod boundMethod, IReadOnlyList<AstNode> argumentNodes)
    {
        foreach (var method in boundMethod.Methods)
            if (TryBuildInvocation(env, method.GetParameters(), argumentNodes, out var args, out var writeBacks))
                return InvokeMethod(env, boundMethod.Target, method, args, writeBacks);

        throw new Exception($"No overload of \"{boundMethod.Methods[0].Name}\" accepts {argumentNodes.Count} argument(s).");
    }

    internal object InvokeReflectedCallable(FishboneEnvironment env, object? target, MethodInfo method, IReadOnlyList<AstNode> argumentNodes)
    {
        if (!TryBuildInvocation(env, method.GetParameters(), argumentNodes, out var args, out var writeBacks))
            throw new Exception($"Expected compatible args for \"{method.Name}\" but got {argumentNodes.Count}.");

        return InvokeMethod(env, target, method, args, writeBacks);
    }

    private object InvokeMethod(
        FishboneEnvironment env,
        object? target,
        MethodInfo method,
        object?[] args,
        List<(string Name, int Index)> writeBacks)
    {
        try
        {
            var result = method.Invoke(target, args);
            foreach (var writeBack in writeBacks)
                env.Assign(writeBack.Name, args[writeBack.Index]!);

            return result!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
    }

    private bool TryBuildInvocation(
        FishboneEnvironment env,
        ParameterInfo[] parameters,
        IReadOnlyList<AstNode> argumentNodes,
        out object?[] args,
        out List<(string Name, int Index)> writeBacks)
    {
        args = new object?[parameters.Length];
        writeBacks = [];

        if (parameters.Length != argumentNodes.Count)
            return false;

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var parameterType = parameter.ParameterType;
            var isByRef = parameterType.IsByRef;
            var targetType = isByRef
                ? parameterType.GetElementType()!
                : parameterType;

            if (parameter.IsOut)
            {
                if (argumentNodes[i] is not IdentifierNode identifier)
                    throw new Exception($"Out argument \"{parameter.Name}\" must be a variable.");

                env.GetValue(identifier.Name);
                args[i] = GetDefaultValue(targetType);
                writeBacks.Add((identifier.Name, i));
                continue;
            }

            object? rawArg;
            if (isByRef)
            {
                if (argumentNodes[i] is not IdentifierNode identifier)
                    throw new Exception($"Ref argument \"{parameter.Name}\" must be a variable.");

                rawArg = env.GetValue(identifier.Name);
                writeBacks.Add((identifier.Name, i));
            }
            else
            {
                rawArg = Evaluate(env, argumentNodes[i]);
            }

            if (!TryConvertArgument(rawArg, targetType, out var convertedArg))
                return false;

            args[i] = convertedArg;
        }

        return true;
    }

    private static object? GetDefaultValue(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
    }

    private static bool TryConvertArgument(object? rawArg, Type targetType, out object? convertedArg)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var conversionType = nullableType ?? targetType;

        if (rawArg is null)
        {
            convertedArg = GetDefaultValue(conversionType);
            return !conversionType.IsValueType || nullableType is not null || convertedArg is not null;
        }

        if (targetType.IsInstanceOfType(rawArg))
        {
            convertedArg = rawArg;
            return true;
        }

        try
        {
            if (conversionType.IsEnum)
            {
                convertedArg = rawArg is string enumName
                    ? Enum.Parse(conversionType, enumName)
                    : Enum.ToObject(conversionType, rawArg);
                return true;
            }

            if (rawArg is IConvertible && typeof(IConvertible).IsAssignableFrom(conversionType))
            {
                convertedArg = Convert.ChangeType(rawArg, conversionType);
                return true;
            }
        }
        catch
        {
            convertedArg = null;
            return false;
        }

        convertedArg = null;
        return false;
    }

    internal object EvaluateListNode(FishboneEnvironment env, ListNode node)
    {
        return node.Elements.Select(i => Evaluate(env, i)).ToList();
    }

    internal object EvaluateDictionaryNode(FishboneEnvironment env, DictionaryNode node)
    {
        Dictionary<object, object?> newDict = [];
        foreach (var item in node.Pairs)
            newDict.Add(Evaluate(env, item.Key), Evaluate(env, item.Value));
        return newDict;
    }

    internal object EvaluateIndexingNode(FishboneEnvironment env, IndexingNode node)
    {
        var target = Evaluate(env, node.Target);
        var index = Evaluate(env, node.Index);

        if (target is IList list)
            return list[(int)index]!;
        else if (target is IDictionary dict)
            return dict[index]!;
        else
            throw new Exception("Object is not indexable");
    }

    internal object EvaluateMemberAccessNode(FishboneEnvironment env,  MemberAccessNode node)
    {
        var target = Evaluate(env, node.Target);
        if (target is null)
            throw new Exception($"Cannot access member \"{node.MemberName}\" on null.");

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var type = target.GetType();

        var property = type
            .GetProperties(flags)
            .FirstOrDefault(prop => prop.Name == node.MemberName && prop.GetIndexParameters().Length == 0);
        if (property is not null)
            return property.GetValue(target)!;

        var field = type
            .GetFields(flags)
            .FirstOrDefault(fieldInfo => fieldInfo.Name == node.MemberName);
        if (field is not null)
            return field.GetValue(target)!;

        var methods = type
            .GetMethods(flags)
            .Where(method => method.Name == node.MemberName && !method.IsSpecialName)
            .ToArray();
        if (methods.Length > 0)
            return new BoundMethod(target, methods);

        throw new Exception($"Type \"{type.Name}\" does not have a public member named \"{node.MemberName}\".");
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