// --------------------------------------------------------------------------------
// FishboneDescription.cs
//
// a read-only description of what a configuration puts into scripts, for tools
// like completion, signature help and the static analyzer.
// --------------------------------------------------------------------------------

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
    public IReadOnlyList<FishboneSymbol> Symbols => throw new NotImplementedException();

    /// <summary>
    /// The members a script reaches with <c>.</c>: the static members of a registered type, or the
    /// instance members of a value. They match what the interpreter resolves.
    /// </summary>
    public IReadOnlyList<FishboneMember> Members(Type type, bool isStatic) => throw new NotImplementedException();
}
