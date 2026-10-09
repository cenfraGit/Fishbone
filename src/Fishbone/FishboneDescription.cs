// --------------------------------------------------------------------------------
// FishboneDescription.cs
//
// a read-only description of what a configuration puts into scripts, for tools
// like completion, signature help and the static analyzer.
// --------------------------------------------------------------------------------

using System.Reflection;
using Fishbone.Interpreter;

namespace Fishbone;

public enum FishboneSymbolKind
{
    /// <summary>A script variable from <see cref="FishboneConfiguration.Values"/>.</summary>
    Value,
    /// <summary>A callable built-in: a delegate, a method group or a host callable.</summary>
    Function,
    /// <summary>A registered .NET type, called to construct it.</summary>
    Type,
    /// <summary>Any other built-in, like a number or an object.</summary>
    Constant
}

public enum FishboneMemberKind
{
    Property,
    Field,
    Method
}

/// <summary>A name the configuration puts into scripts.</summary>
/// <param name="Type">
/// The value's type for a value or constant, the registered type itself for a type, and the
/// callable's .NET type for a function.
/// </param>
/// <param name="Signatures">
/// The overloads of a function, or the constructors of a type. Empty for values and constants.
/// </param>
public sealed record FishboneSymbol(
    string Name,
    FishboneSymbolKind Kind,
    Type Type,
    IReadOnlyList<FishboneSignature> Signatures);

/// <summary>A member a script can reach with <c>.</c>.</summary>
/// <param name="Type">The property or field type, or the return type of a method.</param>
/// <param name="CanWrite">Whether a script can assign it, following the member assignment rules.</param>
/// <param name="Signatures">One per overload for a method. Empty for properties and fields.</param>
public sealed record FishboneMember(
    string Name,
    FishboneMemberKind Kind,
    Type Type,
    bool CanWrite,
    IReadOnlyList<FishboneSignature> Signatures);

/// <summary>One way to call a function, method or constructor.</summary>
public sealed record FishboneSignature(
    IReadOnlyList<FishboneSignatureParameter> Parameters,
    Type ReturnType);

/// <summary>A parameter of a <see cref="FishboneSignature"/>.</summary>
/// <param name="Type">The parameter type. For out and ref parameters, the type without the reference.</param>
public sealed record FishboneSignatureParameter(
    string Name,
    Type Type,
    ParameterDirection Direction,
    bool HasDefault,
    object? DefaultValue);

/// <summary>What a configuration puts into scripts. Made by <see cref="FishboneConfiguration.Describe"/>.</summary>
public sealed class FishboneDescription
{
    internal FishboneDescription(FishboneConfiguration configuration)
    {
        // a value shadows a built-in with the same name, so it's the one a script sees
        Symbols = configuration.Values
            .Select(entry => new FishboneSymbol(entry.Key, FishboneSymbolKind.Value, entry.Value.GetType(), []))
            .Concat(configuration.BuiltIns
                .Where(entry => !configuration.Values.ContainsKey(entry.Key))
                .Select(entry => DescribeBuiltIn(entry.Key, entry.Value)))
            .ToArray();
        ConvertedTypes = configuration.TypeConverters
            .Where(entry => entry.Value.FromNet is not null)
            .Select(entry => entry.Key)
            .ToArray();
    }

    public IReadOnlyList<FishboneSymbol> Symbols { get; }

    // types a registered converter changes on their way back from a call, so a call's
    // declared return type isn't what the script gets
    internal IReadOnlyList<Type> ConvertedTypes { get; }

    /// <summary>
    /// The members a script reaches with <c>.</c>: the static members of a registered type, or the
    /// instance members of a value. They match what the interpreter resolves.
    /// </summary>
    public IReadOnlyList<FishboneMember> Members(Type type, bool isStatic) =>
        ReflectionCache.ListMembers(type, isStatic)
            .Select(member => DescribeMember(member.Name, member.Lookup))
            .ToArray();

    private static FishboneSymbol DescribeBuiltIn(string name, object value) => value switch
    {
        Delegate function => new(name, FishboneSymbolKind.Function, value.GetType(), [DescribeDelegate(function)]),
        BoundMethod methods => new(name, FishboneSymbolKind.Function, value.GetType(),
            methods.Methods.Select(method => DescribeMethod(method, method.ReturnType)).ToArray()),
        IManualCallable callable => new(name, FishboneSymbolKind.Function, value.GetType(), [DescribeManual(callable)]),
        RegisteredType registered => new(name, FishboneSymbolKind.Type, registered.Type, DescribeConstructors(registered.Type)),
        _ => new(name, FishboneSymbolKind.Constant, value.GetType(), [])
    };

    // a delegate is called through its Invoke, which carries the delegate type's defaults. the
    // names come from the method behind it, the way call errors name them
    private static FishboneSignature DescribeDelegate(Delegate function)
    {
        var invoke = function.GetType().GetMethod("Invoke")!;
        var count = ReflectionCache.GetParameters(invoke).Length;
        var inner = ReflectionCache.GetParameters(function.Method);
        var names = inner.Length >= count ? inner[^count..].Select(parameter => parameter.Name).ToArray() : null;
        return DescribeMethod(invoke, invoke.ReturnType, names);
    }

    private static IReadOnlyList<FishboneSignature> DescribeConstructors(Type type)
    {
        var constructors = ReflectionCache.GetConstructors(type)
            .Select(constructor => DescribeMethod(constructor, type))
            .ToList();

        // a struct always has an empty constructor, but reflection doesn't list it
        if (type.IsValueType && !type.IsEnum && constructors.All(signature => signature.Parameters.Count > 0))
            constructors.Add(new FishboneSignature([], type));

        return constructors.OrderBy(signature => signature.Parameters.Count).ToArray();
    }

    private static FishboneSignature DescribeMethod(MethodBase method, Type returnType, string?[]? names = null)
    {
        var parameters = ReflectionCache.GetParameters(method).Select((parameter, i) =>
        {
            var type = parameter.ParameterType;
            var direction = !type.IsByRef ? ParameterDirection.In
                : parameter.IsOut ? ParameterDirection.Out
                : ParameterDirection.Ref;
            return new FishboneSignatureParameter(
                names?[i] ?? parameter.Name ?? $"arg{i + 1}",
                type.IsByRef ? type.GetElementType()! : type,
                direction,
                parameter.HasDefaultValue,
                parameter.HasDefaultValue ? parameter.DefaultValue : null);
        }).ToArray();
        return new FishboneSignature(parameters, returnType);
    }

    private static FishboneSignature DescribeManual(IManualCallable callable) => new(
        callable.Parameters
            .Select(parameter => new FishboneSignatureParameter(parameter.Name, parameter.Type, parameter.Direction, false, null))
            .ToArray(),
        typeof(object));

    // writability follows member assignment: a public setter, or a field that isn't readonly or const
    private static FishboneMember DescribeMember(string name, MemberLookup lookup)
    {
        if (lookup.Property is { } property)
            return new(name, FishboneMemberKind.Property, property.PropertyType, property.SetMethod?.IsPublic == true, []);

        if (lookup.Field is { } field)
            return new(name, FishboneMemberKind.Field, field.FieldType, !field.IsInitOnly && !field.IsLiteral, []);

        var methods = lookup.Methods!;
        return new(name, FishboneMemberKind.Method, methods[0].ReturnType, false,
            methods.Select(method => DescribeMethod(method, method.ReturnType)).ToArray());
    }
}
