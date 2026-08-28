// --------------------------------------------------------------------------------
// HostCallables.cs
//
// the callable shapes a host or plugin deals with directly: a hand-built callable
// with a typed in/out/ref signature, a method bound to a .NET object, and a type
// registered so scripts can construct it.
// --------------------------------------------------------------------------------

using System.Reflection;

namespace Fishbone;

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