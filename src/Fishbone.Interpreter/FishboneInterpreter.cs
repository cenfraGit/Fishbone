// --------------------------------------------------------------------------------
// FishboneInterpreter.cs
//
// the tree-walking interpreter implementation used to evaluate a fishbone AST.
//
// there's mainly a single Evaluate method which takes in an AstNode and dispatches
// it to the correct Evaluate* method. it can be used to evaluate a whole program
// --------------------------------------------------------------------------------

using Fishbone.Core;
using Fishbone.Debugging;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Fishbone.Interpreter;

public class FishboneInterpreter
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    private readonly CancellationToken _cancellationToken;
    private readonly IFishboneDebugger _debugger;
    private readonly IReadOnlyDictionary<Type, FishboneTypeConverter> _typeConverters;
    private static readonly object DebuggerReportedKey = new();

    // dynamic nesting depth of script 'try' blocks: while > 0, exceptions are not reported to
    // the debugger because the script will observe them (or they re-surface past the try)
    private int _tryDepth;

    // exceptions currently bound by enclosing catch blocks; the top is what a bare 'throw;' rethrows
    private readonly Stack<Exception> _activeCatchExceptions = new();

    // when false, the '.' operator is disabled entirely (see FishboneConfiguration.EnableMemberAccess)
    private readonly bool _enableMemberAccess;

    // --------------------------------------------------------------------------------
    // constructors
    // --------------------------------------------------------------------------------

    public FishboneInterpreter(
        CancellationToken cancellationToken = default,
        IFishboneDebugger? debugger = null,
        IReadOnlyDictionary<Type, FishboneTypeConverter>? typeConverters = null,
        bool enableMemberAccess = true)
    {
        _cancellationToken = cancellationToken;
        _debugger = debugger ?? NullFishboneDebugger.Instance;
        _typeConverters = typeConverters ?? new Dictionary<Type, FishboneTypeConverter>();
        _enableMemberAccess = enableMemberAccess;
    }

    // --------------------------------------------------------------------------------
    // dispatch
    // --------------------------------------------------------------------------------

    public object Evaluate(FishboneEnvironment env, AstNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

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
            CastNode castNode => EvaluateCast(env, castNode)!,
            InterpolatedStringNode interpolated => EvaluateInterpolatedString(env, interpolated),
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
            TryNode tryNode => EvaluateTry(env, tryNode),
            ThrowNode throwNode => EvaluateThrow(env, throwNode),
                _ => throw new NotImplementedException($"Execution for {node.GetType().Name} not yet implemented.")
            };
        }
        catch (Exception exception) when (ShouldReport(exception))
        {
            // while inside a script 'try', skip debugger reporting: the script observes the
            // exception through 'catch', or it is reported once it escapes past the try
            if (_tryDepth == 0)
                _debugger.OnRuntimeException(exception, node, env);

            if (exception is FishboneRuntimeException { Line: > 0 })
            {
                if (_tryDepth == 0)
                    exception.Data[DebuggerReportedKey] = true;
                throw;
            }

            // attach the failing node's location: language-diagnosed errors are thrown with a
            // message only (and keep a null inner), while foreign exceptions become the inner
            var wrapped = exception is FishboneRuntimeException unlocated
                ? new FishboneRuntimeException(unlocated.Message, node.Line, node.Column, unlocated.InnerException)
                : new FishboneRuntimeException(exception.Message, node.Line, node.Column, exception);
            if (_tryDepth == 0)
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

    // --------------------------------------------------------------------------------
    // declarations and assignment
    // --------------------------------------------------------------------------------

    internal object EvaluateDeclaration(FishboneEnvironment env, DeclarationNode node)
    {
        // the value is bound whole: a list on the right stays a list
        object value = Evaluate(env, node.Value);
        env.Declare(node.Name, value);
        return value;
    }

    internal object EvaluateAssignment(FishboneEnvironment env, AssignmentNode node)
    {
        object value = Evaluate(env, node.Value);
        env.Assign(node.Name, value);
        return value;
    }

    internal object EvaluateIndexedAssignment(FishboneEnvironment env, IndexedAssignmentNode node)
    {
        object? target = Evaluate(env, node.Target);
        object? index = Evaluate(env, node.Index);
        object? value = Evaluate(env, node.Value);

        SetIndexedValue(target, index, value);
        return value!;
    }

    // --------------------------------------------------------------------------------
    // operators
    // --------------------------------------------------------------------------------

    internal object EvaluateUnary(FishboneEnvironment env, UnaryOpNode node)
    {
        dynamic right = Evaluate(env, node.Right);

        return node.Operator switch
        {
            "-" => -right,
            "not" => !IsTruthy(right),
            _ => throw new FishboneRuntimeException($"Unknown unary operator: {node.Operator}")
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
            // '/' is always true division, whatever the integer widths involved
            if (left is int or long && right is int or long)
                return Convert.ToDouble(left) / Convert.ToDouble(right);
            return left / right;
        }

        // equality never throws on mismatched types. numbers compare by value
        static bool AreEqual(object? left, object? right)
        {
            if (left is int or long or double && right is int or long or double)
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
            _ => throw new FishboneRuntimeException($"Unknown binary operator: {node.Operator}")
        };
    }

    // --------------------------------------------------------------------------------
    // casts
    // --------------------------------------------------------------------------------

    // names accepted as cast targets when the environment doesn't resolve them to a type;
    // "int" etc. normally resolve to the conversion builtins, which are functions, not types
    private static readonly Dictionary<string, Type> PrimitiveTypeNames = new(StringComparer.Ordinal)
    {
        ["int"] = typeof(int),
        ["double"] = typeof(double),
        ["string"] = typeof(string),
        ["bool"] = typeof(bool),
    };

    internal object? EvaluateCast(FishboneEnvironment env, CastNode node)
    {
        var targetType = ResolveCastTargetType(env, node);
        var value = Evaluate(env, node.Value);

        if (value is null)
            return null;
        if (targetType.IsInstanceOfType(value))
            return value;
        return TryConvertArgument(value, targetType, out var converted) ? converted : null;
    }

    private Type ResolveCastTargetType(FishboneEnvironment env, CastNode node)
    {
        if (env.TryGetValue(node.TypeName, out var resolved))
        {
            if (resolved is RegisteredType registeredType)
                return registeredType.Type;
            if (resolved is Type type)
                return type;
        }

        if (PrimitiveTypeNames.TryGetValue(node.TypeName, out var primitive))
            return primitive;

        throw new FishboneRuntimeException(
            $"'{node.TypeName}' is not a type; casting requires a registered type (AddType) or one of: {string.Join(", ", PrimitiveTypeNames.Keys)}.",
            node.Line, node.Column);
    }

    // --------------------------------------------------------------------------------
    // strings
    // --------------------------------------------------------------------------------

    internal object EvaluateInterpolatedString(FishboneEnvironment env, InterpolatedStringNode node)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in node.Parts)
        {
            var value = Evaluate(env, part);
            builder.Append(value switch
            {
                null => string.Empty,
                string text => text,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            });
        }
        return builder.ToString();
    }

    // --------------------------------------------------------------------------------
    // control flow
    // --------------------------------------------------------------------------------

    internal object EvaluateProgram(FishboneEnvironment env, ProgramNode node)
    {
        object lastValue = null!;
        foreach (var statement in node.Statements)
            lastValue = Evaluate(env, statement);
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
            _ => throw new FishboneRuntimeException($"Object of type '{iterable.GetType().Name}' is not iterable.")
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
            throw new FishboneRuntimeException("For: step can't be zero.");

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

    internal object EvaluateReturn(FishboneEnvironment env, ReturnNode node)
    {
        // a bare "return;" carries null; otherwise the value travels as is
        throw new ReturnException(node.Value is null ? null : Evaluate(env, node.Value));
    }

    internal object EvaluateBreak(FishboneEnvironment env, BreakNode node)
    {
        throw new BreakException();
    }

    internal object EvaluateContinue(FishboneEnvironment env, ContinueNode node)
    {
        throw new ContinueException();
    }

    internal object EvaluateTry(FishboneEnvironment env, TryNode node)
    {
        try
        {
            try
            {
                _tryDepth++;
                return Evaluate(env, node.TryBlock);
            }
            finally
            {
                _tryDepth--;
            }
        }
        catch (Exception exception) when (node.CatchBlock is not null && IsCatchableByScript(exception))
        {
            var catchEnv = new FishboneEnvironment(env);
            if (node.ExceptionName is not null)
                catchEnv.Declare(node.ExceptionName, UnwrapForScript(exception));

            _activeCatchExceptions.Push(exception);
            try
            {
                return Evaluate(catchEnv, node.CatchBlock);
            }
            finally
            {
                _activeCatchExceptions.Pop();
            }
        }
        finally
        {
            if (node.FinallyBlock is not null)
                Evaluate(env, node.FinallyBlock);
        }
    }

    internal object EvaluateThrow(FishboneEnvironment env, ThrowNode node)
    {
        if (node.Value is null)
        {
            if (_activeCatchExceptions.Count == 0)
                throw new FishboneRuntimeException(
                    "A bare 'throw;' is only valid inside a catch block.", node.Line, node.Column);
            ExceptionDispatchInfo.Capture(_activeCatchExceptions.Peek()).Throw();
        }

        var value = Evaluate(env, node.Value!);
        if (value is Exception exception)
            throw exception;
        throw new FishboneScriptException(value);
    }

    // --------------------------------------------------------------------------------
    // functions and calls
    // --------------------------------------------------------------------------------

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
        if (callee is FishboneFunction fishboneFunction)
            return InvokeFishboneFunction(env, fishboneFunction, argumentNodes);

        if (callee is Delegate csharpDelegate)
            return InvokeReflectedCallable(env, csharpDelegate.Target, csharpDelegate.Method, argumentNodes);

        if (callee is BoundMethod boundMethod)
            return InvokeBoundMethod(env, boundMethod, argumentNodes);

        if (callee is IManualCallable manualCallable)
            return InvokeManualCallable(env, manualCallable, argumentNodes);

        if (callee is RegisteredType registeredType)
            return InvokeConstructorOverload(env, registeredType, argumentNodes);

        if (callee is null)
            throw new FishboneRuntimeException("Cannot call null. Only functions and methods are callable.");

        throw new FishboneRuntimeException($"Object of type '{callee.GetType().Name}' is not callable.");
    }

    internal object InvokeBoundMethod(FishboneEnvironment env, BoundMethod boundMethod, IReadOnlyList<ArgumentNode> argumentNodes) =>
        InvokeBestOverload(env, boundMethod.Target, boundMethod.Methods, argumentNodes, boundMethod.Methods[0].Name);

    internal object InvokeReflectedCallable(FishboneEnvironment env, object? target, MethodInfo method, IReadOnlyList<ArgumentNode> argumentNodes) =>
        InvokeBestOverload(env, target, [method], argumentNodes, method.Name);

    internal object InvokeConstructorOverload(FishboneEnvironment env, RegisteredType registeredType, IReadOnlyList<ArgumentNode> argumentNodes)
    {
        var constructors = ReflectionCache.GetConstructors(registeredType.Type);
        if (constructors.Length == 0)
            throw new FishboneRuntimeException($"Type '{registeredType.Type.Name}' has no public constructor to call.");

        return InvokeBestOverload(env, target: null, constructors, argumentNodes, registeredType.Type.Name);
    }

    /// <summary>
    /// Invokes a Fishbone script-defined function. Like the <see cref="IManualCallable"/> path there
    /// is a single fixed signature, so arguments are bound positionally with no overload resolution.
    /// Unlike it, script parameters are untyped, so nothing is converted on the way in. An
    /// <c>out</c> parameter is seeded as <c>null</c> inside the callee; after the body finishes the
    /// function copies the final value of every <c>out</c>/<c>ref</c> parameter back into the
    /// argument buffer, and those land in the caller's variables through the shared
    /// <see cref="WriteBackByRefArguments"/> helper.
    /// </summary>
    internal object InvokeFishboneFunction(
        FishboneEnvironment env,
        FishboneFunction function,
        IReadOnlyList<ArgumentNode> argumentNodes)
    {
        var parameters = function.Parameters;
        if (argumentNodes.Count != parameters.Count)
            throw new FishboneRuntimeException($"Expected {parameters.Count} args but got {argumentNodes.Count}.");

        var args = new object?[parameters.Count];
        var writeBacks = new List<(string Name, int Index, bool IsOut)>();

        for (int i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var argument = argumentNodes[i];

            switch (parameter.Modifier)
            {
                case ArgumentModifier.Out:
                    if (argument.Modifier != ArgumentModifier.Out)
                        throw new FishboneRuntimeException($"Parameter '{parameter.Name}' is an out parameter; pass the argument with 'out'.");
                    if (argument.Value is not IdentifierNode outTarget)
                        throw new FishboneRuntimeException($"Out argument '{parameter.Name}' must be a variable.");
                    // an out parameter starts as null in the callee; the caller's value is not read
                    args[i] = null;
                    writeBacks.Add((outTarget.Name, i, true));
                    break;

                case ArgumentModifier.Ref:
                    if (argument.Modifier != ArgumentModifier.Ref)
                        throw new FishboneRuntimeException($"Parameter '{parameter.Name}' is a ref parameter; pass the argument with 'ref'.");
                    if (argument.Value is not IdentifierNode refTarget)
                        throw new FishboneRuntimeException($"Ref argument '{parameter.Name}' must be a variable.");
                    // evaluating the identifier also enforces that the caller's variable exists
                    args[i] = Evaluate(env, argument.Value);
                    writeBacks.Add((refTarget.Name, i, false));
                    break;

                default: // by value
                    if (argument.Modifier != ArgumentModifier.None)
                        throw new FishboneRuntimeException($"Parameter '{parameter.Name}' is passed by value; remove '{argument.Modifier.ToString().ToLowerInvariant()}'.");
                    args[i] = Evaluate(env, argument.Value);
                    break;
            }
        }

        // the body mutates args for out/ref parameters, so the shared write-back can run against it
        var result = function.Call(this, args);
        WriteBackByRefArguments(env, args, writeBacks);
        return result;
    }

    /// <summary>
    /// Invokes a host-supplied <see cref="IManualCallable"/>. Unlike the reflection path there is a
    /// single fixed signature, so no overload resolution is needed; arguments are bound positionally,
    /// converted through the same registered-converter logic as .NET calls, and out/ref results are
    /// written back via the shared <see cref="WriteBackByRefArguments"/> helper.
    /// </summary>
    internal object InvokeManualCallable(
        FishboneEnvironment env,
        IManualCallable callable,
        IReadOnlyList<ArgumentNode> argumentNodes)
    {
        var parameters = callable.Parameters;
        if (argumentNodes.Count != parameters.Count)
            throw new FishboneRuntimeException($"Expected {parameters.Count} argument(s) but got {argumentNodes.Count}.");

        var args = new object?[parameters.Count];
        var writeBacks = new List<(string Name, int Index, bool IsOut)>();

        for (int i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var argument = argumentNodes[i];

            switch (parameter.Direction)
            {
                case ParameterDirection.Out:
                    if (argument.Modifier != ArgumentModifier.Out)
                        throw new FishboneRuntimeException($"Parameter '{parameter.Name}' is an out parameter; pass the argument with 'out'.");
                    if (argument.Value is not IdentifierNode outTarget)
                        throw new FishboneRuntimeException($"Out argument '{parameter.Name}' must be a variable.");
                    // an out parameter introduces or overwrites the variable; pass a default placeholder
                    args[i] = GetDefaultValue(parameter.Type);
                    writeBacks.Add((outTarget.Name, i, true));
                    break;

                case ParameterDirection.Ref:
                    if (argument.Modifier != ArgumentModifier.Ref)
                        throw new FishboneRuntimeException($"Parameter '{parameter.Name}' is a ref parameter; pass the argument with 'ref'.");
                    if (argument.Value is not IdentifierNode)
                        throw new FishboneRuntimeException($"Ref argument '{parameter.Name}' must be a variable.");
                    var refRaw = Evaluate(env, argument.Value);
                    if (ConvertArgument(refRaw, parameter.Type, out var refConverted) == ArgumentMatch.None)
                        throw new FishboneRuntimeException(DescribeConversionFailure(i, refRaw, parameter.Name, parameter.Type));
                    args[i] = refConverted;
                    writeBacks.Add((((IdentifierNode)argument.Value).Name, i, false));
                    break;

                default: // In
                    if (argument.Modifier != ArgumentModifier.None)
                        throw new FishboneRuntimeException($"Parameter '{parameter.Name}' is passed by value; remove '{argument.Modifier.ToString().ToLowerInvariant()}'.");
                    var raw = Evaluate(env, argument.Value);
                    if (ConvertArgument(raw, parameter.Type, out var converted) == ArgumentMatch.None)
                        throw new FishboneRuntimeException(DescribeConversionFailure(i, raw, parameter.Name, parameter.Type));
                    args[i] = converted;
                    break;
            }
        }

        object? result = callable.Invoke(args);
        WriteBackByRefArguments(env, args, writeBacks);
        return ApplyFromNetConverter(result)!;
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

    // --------------------------------------------------------------------------------
    // overload resolution and arg conversion
    // --------------------------------------------------------------------------------

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
                throw new FishboneRuntimeException(deferredDiagnostic);
            throw new FishboneRuntimeException($"No overload of '{methodName}' accepts {argumentNodes.Count} argument(s).");
        }

        if (ambiguous)
            throw new FishboneRuntimeException($"Call to '{methodName}' with {argumentNodes.Count} argument(s) is ambiguous between multiple overloads.");

        return best switch
        {
            ConstructorInfo constructor => InvokeConstructor(env, constructor, bestArgs!, bestWriteBacks!),
            MethodInfo method => InvokeMethod(env, target, method, bestArgs!, bestWriteBacks!),
            _ => throw new FishboneRuntimeException($"Cannot invoke member '{methodName}'.")
        };
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
                    diagnostic ??= $"No argument supplied for parameter '{parameter.Name}', which has no default value.";
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
                    diagnostic = $"Parameter '{parameter.Name}' is an out parameter; pass the argument with 'out'.";
                    return false;
                }

                if (argument.Value is not IdentifierNode outTarget)
                {
                    diagnostic = $"Out argument '{parameter.Name}' must be a variable.";
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
                    diagnostic = $"Parameter '{parameter.Name}' is a ref parameter; pass the argument with 'ref'.";
                    return false;
                }

                if (argument.Value is not IdentifierNode refTarget)
                {
                    diagnostic = $"Ref argument '{parameter.Name}' must be a variable.";
                    return false;
                }

                var refMatch = ConvertArgument(rawArgs[i], targetType, out var refConverted);
                if (refMatch == ArgumentMatch.None)
                {
                    diagnostic ??= DescribeConversionFailure(i, rawArgs[i], parameter.Name!, targetType);
                    return false;
                }

                score += (int)refMatch;
                args[i] = refConverted;
                writeBacks.Add((refTarget.Name, i, false));
                continue;
            }

            if (argument.Modifier != ArgumentModifier.None)
            {
                diagnostic = $"Parameter '{parameter.Name}' is passed by value; remove '{argument.Modifier.ToString().ToLowerInvariant()}'.";
                return false;
            }

            var match = ConvertArgument(rawArgs[i], targetType, out var convertedArg);
            if (match == ArgumentMatch.None)
            {
                diagnostic ??= DescribeConversionFailure(i, rawArgs[i], parameter.Name!, targetType);
                return false;
            }

            score += (int)match;
            args[i] = convertedArg;
        }

        return true;
    }

    // builds the diagnostic shown when an argument cannot be converted to its parameter type
    private static string DescribeConversionFailure(int index, object? rawArg, string parameterName, Type targetType) =>
        $"Argument {index + 1} of type '{rawArg?.GetType().Name ?? "null"}' is not compatible with parameter " +
        $"'{parameterName}' of type '{targetType.Name}'.";

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
                convertedArg = Convert.ChangeType(rawArg, conversionType, CultureInfo.InvariantCulture);
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

    // --------------------------------------------------------------------------------
    // collections
    // --------------------------------------------------------------------------------

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

    // --------------------------------------------------------------------------------
    // indexing
    // --------------------------------------------------------------------------------

    internal object EvaluateIndexingNode(FishboneEnvironment env, IndexingNode node)
    {
        object? target = Evaluate(env, node.Target);
        object? index = Evaluate(env, node.Index);
        return GetIndexedValue(target, index);
    }

    private object GetIndexedValue(object? target, object? index)
    {
        if (target is null)
            throw new FishboneRuntimeException("Cannot index null.");

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
                throw new FishboneRuntimeException($"Index for type '{target.GetType().Name}' must be compatible with Int32.");

            return list[(int)convertedIndex!]!;
        }

        if (target is IDictionary dictionary)
            return dictionary[index!]!;

        if (indexers.Any(indexer => !indexer.CanRead))
            throw new FishboneRuntimeException($"Indexer on type '{target.GetType().Name}' is write-only.");

        if (indexers.Length > 0)
            throw new FishboneRuntimeException($"No readable indexer on type '{target.GetType().Name}' accepts the supplied index.");

        throw new FishboneRuntimeException($"Object of type '{target.GetType().Name}' is not indexable.");
    }

    private void SetIndexedValue(object? target, object? index, object? value)
    {
        if (target is null)
            throw new FishboneRuntimeException("Cannot assign through an index on null.");

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
                throw new FishboneRuntimeException($"Index for type '{target.GetType().Name}' must be compatible with Int32.");

            object? convertedValue = value;
            Type? elementType = target.GetType().GetElementType();
            if (elementType is not null && !TryConvertArgument(value, elementType, out convertedValue))
                throw new FishboneRuntimeException($"Value is not compatible with element type '{elementType.Name}'.");

            list[(int)convertedIndex!] = convertedValue;
            return;
        }

        if (target is IDictionary dictionary)
        {
            dictionary[index!] = value;
            return;
        }

        if (compatibleReadOnlyIndexer)
            throw new FishboneRuntimeException($"Indexer on type '{target.GetType().Name}' is read-only.");

        if (compatibleIndex)
            throw new FishboneRuntimeException($"Value is not compatible with a writable indexer on type '{target.GetType().Name}'.");

        if (indexers.Length > 0)
            throw new FishboneRuntimeException($"No writable indexer on type '{target.GetType().Name}' accepts the supplied index.");

        throw new FishboneRuntimeException($"Object of type '{target.GetType().Name}' does not support indexed assignment.");
    }

    private static PropertyInfo[] GetSingleParameterIndexers(Type type) =>
        ReflectionCache.GetSingleParameterIndexers(type);

    // --------------------------------------------------------------------------------
    // member access
    // --------------------------------------------------------------------------------

    internal object EvaluateMemberAccessNode(FishboneEnvironment env,  MemberAccessNode node)
    {
        if (!_enableMemberAccess)
            throw new FishboneRuntimeException(
                $"Member access ('.{node.MemberName}') is disabled by the host configuration.");

        var target = Evaluate(env, node.Target);
        if (target is null)
            throw new FishboneRuntimeException($"Cannot access member '{node.MemberName}' on null.");

        var type = target.GetType();
        var member = ReflectionCache.ResolveMember(type, node.MemberName);

        if (member.Property is not null)
            return member.Property.GetValue(target)!;

        if (member.Field is not null)
            return member.Field.GetValue(target)!;

        if (member.Methods is not null)
            return new BoundMethod(target, member.Methods);

        throw new FishboneRuntimeException($"Type '{type.Name}' does not have a public member named '{node.MemberName}'.");
    }

    // --------------------------------------------------------------------------------
    // misc
    // --------------------------------------------------------------------------------

    // cancellation and the loop/function control-flow signals must never be observable
    // by a script catch
    private static bool IsCatchableByScript(Exception exception) =>
        exception is not OperationCanceledException
            and not ReturnException
            and not BreakException
            and not ContinueException;

    // a script catch binds the actual .NET exception, not the interpreter's location wrapper
    // (reflection's TargetInvocationException is peeled for the same reason)
    private static Exception UnwrapForScript(Exception exception)
    {
        while (exception is FishboneRuntimeException or TargetInvocationException
               && exception.InnerException is not null)
            exception = exception.InnerException;
        return exception;
    }

    internal bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0.0,
        string s => !string.IsNullOrEmpty(s),
        _ => true
    };

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
}