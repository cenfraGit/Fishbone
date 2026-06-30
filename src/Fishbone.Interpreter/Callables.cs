using Fishbone.Core;
using System.Reflection;

namespace Fishbone.Interpreter;

public interface ICallable
{
    int Arity { get; }
    object Call(FishboneInterpreter interpreter, List<object> arguments);
}

/// <summary>Direction of a single <see cref="INativeCallable"/> parameter.</summary>
public enum ParameterDirection
{
    /// <summary>A by-value input; the argument is read and converted before the call.</summary>
    In,
    /// <summary>An output written back to the caller's variable; passed with <c>out</c>.</summary>
    Out,
    /// <summary>An in/out value read before the call and written back after; passed with <c>ref</c>.</summary>
    Ref
}

/// <summary>One parameter of an <see cref="INativeCallable"/> signature.</summary>
public sealed record CallableParameter(string Name, Type Type, ParameterDirection Direction);

/// <summary>
/// A host-supplied callable that declares a typed in/out/ref signature without being a reflected
/// .NET method. The interpreter owns the generic part (evaluating and converting inputs (honouring
/// registered type converters) and writing <c>out</c>/<c>ref</c> results back into script variables)
/// while the implementation supplies only the <see cref="Invoke"/> body. This lets a plugin expose
/// callables that participate in native <c>out</c>/<c>ref</c> syntax even though they are not .NET
/// methods.
/// </summary>
public interface INativeCallable
{
    /// <summary>The parameters, in call-site order.</summary>
    IReadOnlyList<CallableParameter> Parameters { get; }

    /// <summary>
    /// Runs the call. <paramref name="arguments"/> is indexed parallel to <see cref="Parameters"/>;
    /// <see cref="ParameterDirection.In"/> and <see cref="ParameterDirection.Ref"/> slots hold the
    /// converted inputs. The implementation writes each <see cref="ParameterDirection.Out"/> and
    /// <see cref="ParameterDirection.Ref"/> slot back in place, mirroring how a .NET method writes
    /// its by-ref results. Returns an optional result value (or <c>null</c> when there is none).
    /// </summary>
    object? Invoke(object?[] arguments);
}

public class FishboneFunction : ICallable
{
    private readonly FunctionDefinitionNode _definition;
    private readonly FishboneEnvironment _closure;

    public FishboneFunction(FunctionDefinitionNode definition, FishboneEnvironment closure)
    {
        _definition = definition;
        _closure = closure;
    }

    public int Arity => _definition.Parameters.Length;

    public object Call(FishboneInterpreter interpreter, List<object> arguments)
    {
        // new env for function scope
        var envFunction = new FishboneEnvironment(_closure);

        // bind args to names
        for (int i = 0; i < _definition.Parameters.Length; i++)
            envFunction.Declare(_definition.Parameters[i], arguments[i]);

        interpreter.OnFunctionEnter(_definition.Name, envFunction);
        try
        {
            try
            {
                interpreter.EvaluateBlock(envFunction, _definition.Body);
            }
            catch (ReturnException ret)
            {
                return ret.Values is List<object> list && list.Count == 1
                    ? list[0]
                    : ret.Values;
            }

            return null!;
        }
        finally
        {
            interpreter.OnFunctionExit(_definition.Name);
        }
    }
}

public sealed class BoundMethod
{
    public BoundMethod(object target, IReadOnlyList<MethodInfo> methods)
    {
        Target = target;
        Methods = methods;
    }

    public object Target { get; }
    public IReadOnlyList<MethodInfo> Methods { get; }
}

/// <summary>
/// A .NET type that scripts may construct by calling its name like a function
/// (for example <c>let p = Point(1, 2);</c>). Registered through
/// <c>FishboneConfiguration.AddType</c>.
/// </summary>
public sealed class RegisteredType
{
    public RegisteredType(Type type)
    {
        Type = type;
    }

    public Type Type { get; }
}