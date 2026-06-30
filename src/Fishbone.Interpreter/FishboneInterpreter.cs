using Fishbone.Core;
using Fishbone.Debugging;
using System.Collections;
using System.Reflection;

namespace Fishbone.Interpreter;

public class FishboneInterpreter
{
    private readonly CancellationToken _cancellationToken;
    private readonly IFishboneDebugger _debugger;
    private readonly IReadOnlyDictionary<Type, FishboneTypeConverter> _typeConverters;
    private static readonly object DebuggerReportedKey = new();

    public FishboneInterpreter(
        CancellationToken cancellationToken = default,
        IFishboneDebugger? debugger = null,
        IReadOnlyDictionary<Type, FishboneTypeConverter>? typeConverters = null)
    {
        _cancellationToken = cancellationToken;
        _debugger = debugger ?? NullFishboneDebugger.Instance;
        _typeConverters = typeConverters ?? new Dictionary<Type, FishboneTypeConverter>();
    }

    /// <summary>
    /// Normalizes a value crossing back into the script: if its runtime type has a registered
    /// converter with a from-direction, applies it; otherwise returns the value unchanged so it
    /// remains an ordinary .NET object the script can interop with.
    /// </summary>
    private object? ApplyFromNetConverter(object? value)
    {
        if (value is not null
            && _typeConverters.TryGetValue(value.GetType(), out var converter)
            && converter.FromNet is not null)
            return converter.FromNet(value);
        return value;
    }

    public object Evaluate(FishboneEnvironment env, AstNode node)
    {
        if (node is null)
            throw new ArgumentNullException(nameof(node));

        _cancellationToken.ThrowIfCancellationRequested();
        _debugger.OnBeforeExecute(node, env);

        try
        {
            return node switch
            {
            ProgramNode programNode => EvaluateProgram(env, programNode),
            LiteralNode literal => literal.Value,
            IdentifierNode identifier => env.GetValue(identifier.Name),
            DeclarationNode declaration => EvaluateDeclaration(env, declaration),
            AssignmentNode assignment => EvaluateAssignment(env, assignment),
            IndexedAssignmentNode indexedAssignment => EvaluateIndexedAssignment(env, indexedAssignment),
            UnaryOpNode unary => EvaluateUnary(env, unary),
            BinaryOpNode binary => EvaluateBinary(env, binary),
            IfNode ifNode => EvaluateIf(env, ifNode),
            WhileNode whileNode => EvaluateWhile(env, whileNode),
            ForeachNode foreachNode => EvaluateForeach(env, foreachNode),
            ForNode forNode => EvaluateFor(env, forNode),
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
        catch (Exception exception) when (ShouldReport(exception))
        {
            _debugger.OnRuntimeException(exception, node, env);
            if (exception is FishboneRuntimeException)
            {
                exception.Data[DebuggerReportedKey] = true;
                throw;
            }
            var wrapped = new FishboneRuntimeException(exception.Message, node.Line, node.Column, exception);
            wrapped.Data[DebuggerReportedKey] = true;
            throw wrapped;
        }
    }

    internal void OnFunctionEnter(string functionName, FishboneEnvironment environment) =>
        _debugger.OnFunctionEnter(functionName, environment);

    internal void OnFunctionExit(string functionName) => _debugger.OnFunctionExit(functionName);

    private static bool ShouldReport(Exception exception) =>
        exception is not OperationCanceledException and not ReturnException and not BreakException and not ContinueException
        && !exception.Data.Contains(DebuggerReportedKey);

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

    internal object EvaluateIndexedAssignment(FishboneEnvironment env, IndexedAssignmentNode node)
    {
        object? target = Evaluate(env, node.Target);
        object? index = Evaluate(env, node.Index);
        object? value = Evaluate(env, node.Value);

        SetIndexedValue(target, index, value);
        return value!;
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

        if (node.Operator == "and")
            return IsTruthy(left) && IsTruthy(Evaluate(env, node.Right));

        if (node.Operator == "or")
            return IsTruthy(left) || IsTruthy(Evaluate(env, node.Right));

        if (node.Operator == "xor")
            return IsTruthy(left) != IsTruthy(Evaluate(env, node.Right));

        dynamic right = Evaluate(env, node.Right);

        static object Divide(dynamic left, dynamic right)
        {
            if (left is int intLeft && right is int intRight)
                return intLeft / (double)intRight;
            return left / right;
        }

        // equality never throws on mismatched types. numbers compare by value
        static bool AreEqual(object? left, object? right)
        {
            if (left is int or double && right is int or double)
                return Convert.ToDouble(left) == Convert.ToDouble(right);
            return Equals(left, right);
        }

        return node.Operator switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" => Divide(left, right),
            "%" => left % right,
            // comparison
            "==" => AreEqual(left, right),
            "!=" => !AreEqual(left, right),
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
            _ => throw new Exception($"Object of type \"{iterable.GetType().Name}\" is not iterable.")
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

    internal object EvaluateFor(FishboneEnvironment env, ForNode node)
    {
        var start = Convert.ToDouble(Evaluate(env, node.Start));
        var end = Convert.ToDouble(Evaluate(env, node.End));

        // 1.0 or -1.0 depending on direction
        var step = (node.Step is null) ? Math.Sign(end - start) : Convert.ToDouble(Evaluate(env, node.Step));

        if (start == end) return null!;

        if (step == 0.0)
            throw new Exception("For: step can't be zero.");

        // true if start is less than end
        // false if start is more than end
        bool forIncremental = (start < end);

        // create new env and declare iterator name
        var loopEnv = new FishboneEnvironment(env);
        object lastValue = null!;

        double i = start; // iterator value
        loopEnv.Declare(node.IteratorName, start);
        while (true)
        {
            // checking phase: use forIncremental (direction) and evaluate status

            // incremental: whenever i is greater than end, stop
            if (forIncremental) { if (i >= end) break; }
            // decremental: whenever i is less than end, stop
            else { if (i <= end) break; }

            try
            {
                lastValue = Evaluate(loopEnv, node.Body);
            }
            catch (ContinueException)
            {
                // update with current vlaue
                i = i + step;
                loopEnv.Assign(node.IteratorName, i);
                continue;
            }
            catch (BreakException)
            {
                break;
            }

            // update with current vlaue
            i = i + step;
            loopEnv.Assign(node.IteratorName, i);
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

    internal object EvaluateCall(FishboneEnvironment env, object callee, IReadOnlyList<ArgumentNode> argumentNodes)
    {
        if (callee is ICallable fishboneFunction)
        {
            if (argumentNodes.Count != fishboneFunction.Arity)
                throw new Exception($"Expected {fishboneFunction.Arity} args but got {argumentNodes.Count}.");

            var evaluatedArgs = new List<object>();
            foreach (var argNode in argumentNodes)
            {
                if (argNode.Modifier != ArgumentModifier.None)
                    throw new Exception($"'{argNode.Modifier.ToString().ToLowerInvariant()}' arguments are only supported when calling .NET methods.");

                evaluatedArgs.Add(Evaluate(env, argNode.Value));
            }

            return fishboneFunction.Call(this, evaluatedArgs);
        }

        if (callee is Delegate csharpDelegate)
            return InvokeReflectedCallable(env, csharpDelegate.Target, csharpDelegate.Method, argumentNodes);

        if (callee is BoundMethod boundMethod)
            return InvokeBoundMethod(env, boundMethod, argumentNodes);

        if (callee is RegisteredType registeredType)
            return InvokeConstructorOverload(env, registeredType, argumentNodes);

        if (callee is null)
            throw new Exception("Cannot call null. Only functions and methods are callable.");

        throw new Exception($"Object of type \"{callee.GetType().Name}\" is not callable.");
    }

    internal object InvokeBoundMethod(FishboneEnvironment env, BoundMethod boundMethod, IReadOnlyList<ArgumentNode> argumentNodes) =>
        InvokeBestOverload(env, boundMethod.Target, boundMethod.Methods, argumentNodes, boundMethod.Methods[0].Name);

    internal object InvokeReflectedCallable(FishboneEnvironment env, object? target, MethodInfo method, IReadOnlyList<ArgumentNode> argumentNodes) =>
        InvokeBestOverload(env, target, [method], argumentNodes, method.Name);

    internal object InvokeConstructorOverload(FishboneEnvironment env, RegisteredType registeredType, IReadOnlyList<ArgumentNode> argumentNodes)
    {
        var constructors = ReflectionCache.GetConstructors(registeredType.Type);
        if (constructors.Length == 0)
            throw new Exception($"Type \"{registeredType.Type.Name}\" has no public constructor to call.");

        return InvokeBestOverload(env, target: null, constructors, argumentNodes, registeredType.Type.Name);
    }

    /// <summary>
    /// Selects the best-matching overload from <paramref name="methods"/> and invokes it.
    /// Arguments are evaluated exactly once, then each candidate is scored by how well every
    /// argument matches its parameter. The highest total score wins.
    /// </summary>
    private object InvokeBestOverload(
        FishboneEnvironment env,
        object? target,
        IReadOnlyList<MethodBase> methods,
        IReadOnlyList<ArgumentNode> argumentNodes,
        string methodName)
    {
        // evaluate every argument once. 'out' arguments are skipped: the receiving variable need
        // not exist yet (the call introduces it), so evaluating it would wrongly fail. 'ref' and
        // by-value arguments are read here, which is why 'ref' requires an already-defined variable.
        var rawArgs = new object?[argumentNodes.Count];
        for (int i = 0; i < argumentNodes.Count; i++)
            rawArgs[i] = argumentNodes[i].Modifier == ArgumentModifier.Out
                ? null
                : Evaluate(env, argumentNodes[i].Value);

        MethodBase? best = null;
        object?[]? bestArgs = null;
        List<(string Name, int Index, bool IsOut)>? bestWriteBacks = null;
        int bestScore = -1;
        int bestDefaultsUsed = int.MaxValue;
        bool ambiguous = false;
        string? deferredDiagnostic = null;

        foreach (var method in methods)
        {
            var parameters = ReflectionCache.GetParameters(method);
            if (!TryBindOverload(parameters, argumentNodes, rawArgs, out var args, out var writeBacks, out var score, out var defaultsUsed, out var diagnostic))
            {
                deferredDiagnostic ??= diagnostic;
                continue;
            }

            // higher score wins; on a tie the overload that relied on fewer defaults wins, so an
            // exact-arity overload is preferred over one that only matched by filling optionals
            bool isBetter = best is null
                || score > bestScore
                || (score == bestScore && defaultsUsed < bestDefaultsUsed);

            if (isBetter)
            {
                best = method;
                bestArgs = args;
                bestWriteBacks = writeBacks;
                bestScore = score;
                bestDefaultsUsed = defaultsUsed;
                ambiguous = false;
            }
            else if (score == bestScore && defaultsUsed == bestDefaultsUsed)
            {
                ambiguous = true;
            }
        }

        if (best is null)
        {
            if (deferredDiagnostic is not null)
                throw new Exception(deferredDiagnostic);
            throw new Exception($"No overload of \"{methodName}\" accepts {argumentNodes.Count} argument(s).");
        }

        if (ambiguous)
            throw new Exception($"Call to \"{methodName}\" with {argumentNodes.Count} argument(s) is ambiguous between multiple overloads.");

        return best switch
        {
            ConstructorInfo constructor => InvokeConstructor(env, constructor, bestArgs!, bestWriteBacks!),
            MethodInfo method => InvokeMethod(env, target, method, bestArgs!, bestWriteBacks!),
            _ => throw new Exception($"Cannot invoke member \"{methodName}\".")
        };
    }

    private object InvokeMethod(
        FishboneEnvironment env,
        object? target,
        MethodInfo method,
        object?[] args,
        List<(string Name, int Index, bool IsOut)> writeBacks)
    {
        var delegateEnv = new FishboneEnvironment(env);
        OnFunctionEnter(method.Name, delegateEnv);
        try
        {
            // span overload is required, it is the only MethodInvoker.Invoke overload that
            // writes by-ref (out/ref) results back into the supplied argument buffer.
            var result = ReflectionCache.GetInvoker(method).Invoke(target, args.AsSpan());
            WriteBackByRefArguments(env, args, writeBacks);

            return ApplyFromNetConverter(result)!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
        finally
        {
            OnFunctionExit(method.Name);
        }
    }

    private object InvokeConstructor(
        FishboneEnvironment env,
        ConstructorInfo constructor,
        object?[] args,
        List<(string Name, int Index, bool IsOut)> writeBacks)
    {
        var typeName = constructor.DeclaringType?.Name ?? "constructor";
        OnFunctionEnter(typeName, new FishboneEnvironment(env));
        try
        {
            var instance = ReflectionCache.GetConstructorInvoker(constructor).Invoke(args.AsSpan());
            WriteBackByRefArguments(env, args, writeBacks);

            return instance!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException ?? ex;
        }
        finally
        {
            OnFunctionExit(typeName);
        }
    }

    /// <summary>
    /// Attempts to bind already-evaluated arguments to a single overload's parameters, producing
    /// the converted argument buffer, the out/ref write-back list, and a total match score.
    /// Returns false when the overload cannot accept the arguments. Does not evaluate argument
    /// expressions (that happens once in the caller) so it can be run against every candidate
    /// overload without repeating side effects.
    /// </summary>
    private bool TryBindOverload(
        ParameterInfo[] parameters,
        IReadOnlyList<ArgumentNode> argumentNodes,
        object?[] rawArgs,
        out object?[] args,
        out List<(string Name, int Index, bool IsOut)> writeBacks,
        out int score,
        out int defaultsUsed,
        out string? diagnostic)
    {
        args = new object?[parameters.Length];
        writeBacks = [];
        score = 0;
        defaultsUsed = 0;
        diagnostic = null;

        // too many arguments can never bind; too few can still bind if the unmatched trailing
        // parameters are optional (filled from their defaults below)
        if (argumentNodes.Count > parameters.Length)
            return false;

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            // no argument was supplied for this parameter: accept the overload only if the parameter
            // is optional, supplying its default value. out/ref parameters are never optional
            if (i >= argumentNodes.Count)
            {
                if (!parameter.HasDefaultValue)
                {
                    diagnostic ??= $"No argument supplied for parameter \"{parameter.Name}\", which has no default value.";
                    return false;
                }

                args[i] = parameter.DefaultValue;
                defaultsUsed++;
                continue;
            }

            var parameterType = parameter.ParameterType;
            var isByRef = parameterType.IsByRef;
            var targetType = isByRef
                ? parameterType.GetElementType()!
                : parameterType;
            var argument = argumentNodes[i];

            if (parameter.IsOut)
            {
                if (argument.Modifier != ArgumentModifier.Out)
                {
                    diagnostic = $"Parameter \"{parameter.Name}\" is an out parameter; pass the argument with 'out'.";
                    return false;
                }

                if (argument.Value is not IdentifierNode outTarget)
                {
                    diagnostic = $"Out argument \"{parameter.Name}\" must be a variable.";
                    return false;
                }

                args[i] = GetDefaultValue(targetType);
                writeBacks.Add((outTarget.Name, i, true));
                // an out parameter consumes no input value, so it does not bias overload scoring
                score += (int)ArgumentMatch.Exact;
                continue;
            }

            if (isByRef)
            {
                if (argument.Modifier != ArgumentModifier.Ref)
                {
                    diagnostic = $"Parameter \"{parameter.Name}\" is a ref parameter; pass the argument with 'ref'.";
                    return false;
                }

                if (argument.Value is not IdentifierNode refTarget)
                {
                    diagnostic = $"Ref argument \"{parameter.Name}\" must be a variable.";
                    return false;
                }

                var refMatch = ConvertArgument(rawArgs[i], targetType, out var refConverted);
                if (refMatch == ArgumentMatch.None)
                {
                    diagnostic ??= DescribeConversionFailure(i, rawArgs[i], parameter, targetType);
                    return false;
                }

                score += (int)refMatch;
                args[i] = refConverted;
                writeBacks.Add((refTarget.Name, i, false));
                continue;
            }

            if (argument.Modifier != ArgumentModifier.None)
            {
                diagnostic = $"Parameter \"{parameter.Name}\" is passed by value; remove '{argument.Modifier.ToString().ToLowerInvariant()}'.";
                return false;
            }

            var match = ConvertArgument(rawArgs[i], targetType, out var convertedArg);
            if (match == ArgumentMatch.None)
            {
                diagnostic ??= DescribeConversionFailure(i, rawArgs[i], parameter, targetType);
                return false;
            }

            score += (int)match;
            args[i] = convertedArg;
        }

        return true;
    }

    // builds the diagnostic shown when an argument cannot be converted to its parameter type
    private static string DescribeConversionFailure(int index, object? rawArg, ParameterInfo parameter, Type targetType) =>
        $"Argument {index + 1} of type \"{rawArg?.GetType().Name ?? "null"}\" is not compatible with parameter " +
        $"\"{parameter.Name}\" of type \"{targetType.Name}\".";

    private void WriteBackByRefArguments(
        FishboneEnvironment env,
        object?[] args,
        List<(string Name, int Index, bool IsOut)> writeBacks)
    {
        foreach (var writeBack in writeBacks)
        {
            // an out/ref value crossing back into the script is normalized the same way a return
            // value is, so a registered type (e.g. a wrapped tuple) becomes a plain script value
            var value = ApplyFromNetConverter(args[writeBack.Index])!;

            // 'out' introduces the variable when it does not already exist; 'ref' (and an 'out'
            // that targets an existing variable) writes through to the existing binding
            if (writeBack.IsOut && !env.IsDefined(writeBack.Name))
                env.Declare(writeBack.Name, value);
            else
                env.Assign(writeBack.Name, value);
        }
    }

    private static object? GetDefaultValue(Type type)
    {
        var targetType = Nullable.GetUnderlyingType(type) ?? type;
        return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
    }

    /// <summary>
    /// Quality of a single argument-to-parameter match, used to rank overloads. Ordered so a
    /// larger value is a better match; <see cref="None"/> means the argument is not accepted.
    /// </summary>
    private enum ArgumentMatch
    {
        None = 0,
        Convertible = 1, // requires explicit conversion (Convert.ChangeType, enum-from-int)
        Assignable = 2,  // reference/interface assignable without conversion (e.g. int -> object)
        Exact = 3        // runtime type already matches the parameter type
    }

    private bool TryConvertArgument(object? rawArg, Type targetType, out object? convertedArg) =>
        ConvertArgument(rawArg, targetType, out convertedArg) != ArgumentMatch.None;

    private ArgumentMatch ConvertArgument(object? rawArg, Type targetType, out object? convertedArg)
    {
        var nullableType = Nullable.GetUnderlyingType(targetType);
        var conversionType = nullableType ?? targetType;

        if (rawArg is null)
        {
            convertedArg = GetDefaultValue(conversionType);
            var nullAccepted = !conversionType.IsValueType || nullableType is not null || convertedArg is not null;
            return nullAccepted ? ArgumentMatch.Assignable : ArgumentMatch.None;
        }

        var rawType = rawArg.GetType();
        if (rawType == targetType || rawType == conversionType)
        {
            convertedArg = rawArg;
            return ArgumentMatch.Exact;
        }

        if (targetType.IsInstanceOfType(rawArg))
        {
            convertedArg = rawArg;
            return ArgumentMatch.Assignable;
        }

        // a host-registered converter handles types the generic path below cannot (anything not
        // IConvertible or an enum); it takes precedence so registered types convert deterministically
        if (_typeConverters.TryGetValue(conversionType, out var converter))
        {
            try
            {
                convertedArg = converter.ToNet(rawArg);
                return ArgumentMatch.Convertible;
            }
            catch
            {
                convertedArg = null;
                return ArgumentMatch.None;
            }
        }

        try
        {
            if (conversionType.IsEnum)
            {
                convertedArg = rawArg is string enumName
                    ? Enum.Parse(conversionType, enumName)
                    : Enum.ToObject(conversionType, rawArg);
                return ArgumentMatch.Convertible;
            }

            if (rawArg is IConvertible && typeof(IConvertible).IsAssignableFrom(conversionType))
            {
                convertedArg = Convert.ChangeType(rawArg, conversionType);
                return ArgumentMatch.Convertible;
            }
        }
        catch
        {
            convertedArg = null;
            return ArgumentMatch.None;
        }

        convertedArg = null;
        return ArgumentMatch.None;
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
        object? target = Evaluate(env, node.Target);
        object? index = Evaluate(env, node.Index);
        return GetIndexedValue(target, index);
    }

    private object GetIndexedValue(object? target, object? index)
    {
        if (target is null)
            throw new Exception("Cannot index null.");

        PropertyInfo[] indexers = GetSingleParameterIndexers(target.GetType());
        foreach (PropertyInfo indexer in indexers)
        {
            if (!indexer.CanRead)
                continue;

            ParameterInfo indexParameter = indexer.GetIndexParameters()[0];
            if (!TryConvertArgument(index, indexParameter.ParameterType, out object? convertedIndex))
                continue;

            try
            {
                return indexer.GetValue(target, [convertedIndex])!;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        if (target is IList list)
        {
            if (!TryConvertArgument(index, typeof(int), out object? convertedIndex))
                throw new Exception($"Index for type \"{target.GetType().Name}\" must be compatible with Int32.");

            return list[(int)convertedIndex!]!;
        }

        if (target is IDictionary dictionary)
            return dictionary[index!]!;

        if (indexers.Any(indexer => !indexer.CanRead))
            throw new Exception($"Indexer on type \"{target.GetType().Name}\" is write-only.");

        if (indexers.Length > 0)
            throw new Exception($"No readable indexer on type \"{target.GetType().Name}\" accepts the supplied index.");

        throw new Exception($"Object of type \"{target.GetType().Name}\" is not indexable.");
    }

    private void SetIndexedValue(object? target, object? index, object? value)
    {
        if (target is null)
            throw new Exception("Cannot assign through an index on null.");

        PropertyInfo[] indexers = GetSingleParameterIndexers(target.GetType());
        bool compatibleReadOnlyIndexer = false;
        bool compatibleIndex = false;

        foreach (PropertyInfo indexer in indexers)
        {
            ParameterInfo indexParameter = indexer.GetIndexParameters()[0];
            if (!TryConvertArgument(index, indexParameter.ParameterType, out object? convertedIndex))
                continue;

            compatibleIndex = true;
            if (!indexer.CanWrite)
            {
                compatibleReadOnlyIndexer = true;
                continue;
            }

            if (!TryConvertArgument(value, indexer.PropertyType, out object? convertedValue))
                continue;

            try
            {
                indexer.SetValue(target, convertedValue, [convertedIndex]);
                return;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        if (target is IList list)
        {
            if (!TryConvertArgument(index, typeof(int), out object? convertedIndex))
                throw new Exception($"Index for type \"{target.GetType().Name}\" must be compatible with Int32.");

            object? convertedValue = value;
            Type? elementType = target.GetType().GetElementType();
            if (elementType is not null && !TryConvertArgument(value, elementType, out convertedValue))
                throw new Exception($"Value is not compatible with element type \"{elementType.Name}\".");

            list[(int)convertedIndex!] = convertedValue;
            return;
        }

        if (target is IDictionary dictionary)
        {
            dictionary[index!] = value;
            return;
        }

        if (compatibleReadOnlyIndexer)
            throw new Exception($"Indexer on type \"{target.GetType().Name}\" is read-only.");

        if (compatibleIndex)
            throw new Exception($"Value is not compatible with a writable indexer on type \"{target.GetType().Name}\".");

        if (indexers.Length > 0)
            throw new Exception($"No writable indexer on type \"{target.GetType().Name}\" accepts the supplied index.");

        throw new Exception($"Object of type \"{target.GetType().Name}\" does not support indexed assignment.");
    }

    private static PropertyInfo[] GetSingleParameterIndexers(Type type) =>
        ReflectionCache.GetSingleParameterIndexers(type);

    internal object EvaluateMemberAccessNode(FishboneEnvironment env,  MemberAccessNode node)
    {
        var target = Evaluate(env, node.Target);
        if (target is null)
            throw new Exception($"Cannot access member \"{node.MemberName}\" on null.");

        var type = target.GetType();
        var member = ReflectionCache.ResolveMember(type, node.MemberName);

        if (member.Property is not null)
            return member.Property.GetValue(target)!;

        if (member.Field is not null)
            return member.Field.GetValue(target)!;

        if (member.Methods is not null)
            return new BoundMethod(target, member.Methods);

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