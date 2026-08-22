// --------------------------------------------------------------------------------
// Callables.cs
//
// a callable is something you can put parentheses after, being
// constructors, methods, functions, etc.
//
// this file defines the different callable kinds.
// there are different "callable" circumstances in practice:
//
// - FishboneFunction: a fb-script defined function
//
// - BoundMethod: a method from a .NET object, (like "obj.DoSomething()").
//                BoundMethod holds the object and a list of matching methods.
//                this is so the interpreter picks the right overload depending
//                on the number of args and types. supports ref/out.
//
// - RegisteredType: basically a constructor call. if the host did
//                   config.AddType<Point>(), the script could construct the
//                   Point object doing Point(100, 200) for example.
//
// - a delegate: this one doesn't have a type in here. you can just pass it via
//               name as long as there are no overloads for the method (in that
//               case you'd have to wrap it in Func<> or something).
//
// - IManualCallable: this callable is built by hand by the host, not a real .net
//                    delegate. its an option because delegates like Func<> can't have
//                    out/ref parameters. the host can describe the parameters
//                    using CallableParameter, ParameterDirection to define the
//                    list of arguments that this callable will take. this one is
//                    used rarely and for some specific cases
//
// --------------------------------------------------------------------------------

using Fishbone.Core;
using System.Reflection;

namespace Fishbone.Interpreter;

// --------------------------------------------------------------------------------
// FishboneFunction
// --------------------------------------------------------------------------------

// callable for pure fishbone script-defined functions.
internal class FishboneFunction
{
    private readonly FunctionDefinitionNode _definition;
    private readonly FishboneEnvironment _closure;

    public FishboneFunction(FunctionDefinitionNode definition, FishboneEnvironment closure)
    {
        _definition = definition;
        _closure = closure;
    }

    public int Arity => _definition.Parameters.Length;

    public IReadOnlyList<ParameterNode> Parameters => _definition.Parameters;

    // runs the function body. "args" is indexed parallel to the definition's parameters and
    // is mutated in place for out/ref parameters, mirroring how a reflected .NET method writes
    // its by-ref results, so the caller can reuse the same write-back path.
    public object Call(FishboneInterpreter interpreter, object?[] args)
    {
        // new env for function scope
        var envFunction = new FishboneEnvironment(_closure);

        // bind args to names. an 'out' parameter starts as null: the body is expected to
        // assign it, but is not required to (an unassigned 'out' just hands back null)
        for (int i = 0; i < _definition.Parameters.Length; i++)
        {
            var parameter = _definition.Parameters[i];
            envFunction.Declare(parameter.Name,
                parameter.Modifier == ArgumentModifier.Out ? null! : args[i]!);
        }

        interpreter.OnFunctionEnter(_definition.Name, envFunction);
        try
        {
            try
            {
                interpreter.EvaluateBlock(envFunction, _definition.Body);
            }
            catch (ReturnException ret)
            {
                HarvestByRefParameters(envFunction, args);
                return ret.Value!;
            }

            HarvestByRefParameters(envFunction, args);
            return null!;
        }
        finally
        {
            interpreter.OnFunctionExit(_definition.Name);
        }
    }

    // copies the final value of each out/ref parameter binding back into the argument buffer,
    // where the caller's write-back picks it up. deliberately not in a finally: if the body
    // throws, nothing is written back, same as a throwing .NET method.
    private void HarvestByRefParameters(FishboneEnvironment envFunction, object?[] args)
    {
        for (int i = 0; i < _definition.Parameters.Length; i++)
        {
            var parameter = _definition.Parameters[i];
            if (parameter.Modifier == ArgumentModifier.None)
                continue;

            args[i] = envFunction.GetValue(parameter.Name);
        }
    }
}

// --------------------------------------------------------------------------------
// IManualCallable
// --------------------------------------------------------------------------------

/// <summary>Direction of a single <see cref="IManualCallable"/> parameter.</summary>
public enum ParameterDirection
{
    /// <summary>A by-value input; the argument is read and converted before the call.</summary>
    In,
    /// <summary>An output written back to the caller's variable; passed with <c>out</c>.</summary>
    Out,
    /// <summary>An in/out value read before the call and written back after; passed with <c>ref</c>.</summary>
    Ref
}

/// <summary>One parameter of an <see cref="IManualCallable"/> signature.</summary>
public sealed record CallableParameter(string Name, Type Type, ParameterDirection Direction);

/// <summary>
/// A host-supplied callable that declares a typed in/out/ref signature without being a reflected
/// .NET method. The interpreter owns the generic part (evaluating and converting inputs (honouring
/// registered type converters) and writing <c>out</c>/<c>ref</c> results back into script variables)
/// while the implementation supplies only the <see cref="Invoke"/> body. This lets a plugin expose
/// callables that participate in native <c>out</c>/<c>ref</c> syntax even though they are not .NET
/// methods.
/// </summary>
public interface IManualCallable
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

// --------------------------------------------------------------------------------
// BoundMethod
// --------------------------------------------------------------------------------

/// <summary>
/// Represents a method from a .NET object. Holds the target (the object) and a list
/// of methods matching the name. The interpreter will look for the most adequate method
/// to call depending on the arg number and types.
/// </summary>
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

// --------------------------------------------------------------------------------
// RegisteredType
// --------------------------------------------------------------------------------

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