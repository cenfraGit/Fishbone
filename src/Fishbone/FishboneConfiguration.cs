// --------------------------------------------------------------------------------
// FishboneConfiguration.cs
//
// a configuration object used to set up a fishbone execution environment.
// --------------------------------------------------------------------------------

using Fishbone.Interpreter;
using System.Globalization;

namespace Fishbone;

public class FishboneConfiguration
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    /// <summary>
    /// When false, scripts cannot use the <c>.</c> operator.
    /// </summary>
    public bool EnableMemberAccess { get; set; } = true;

    /// <summary>
    /// Functions, types, or constants that the script has access to.
    /// Cannot be overwritten (but can be shadowed by Values).
    /// Also not shown in debugger variable section.
    /// </summary>
    public Dictionary<string, object> BuiltIns { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Pre-seeded script variable table.
    /// Shown in debugger variable section.
    /// </summary>
    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Host-registered conversions between script values and .NET types.
    /// </summary>
    public Dictionary<Type, FishboneTypeConverter> TypeConverters { get; } = [];

    // --------------------------------------------------------------------------------
    // constructors
    // --------------------------------------------------------------------------------

    public FishboneConfiguration()
    {
    }

    // --------------------------------------------------------------------------------
    // setup methods
    // --------------------------------------------------------------------------------

    /// <summary>Binds an ambient built-in (function, value, or registered type) under a name.</summary>
    public FishboneConfiguration AddBuiltIn(string name, object value)
    {
        BuiltIns[name] = value;
        return this;
    }

    /// <summary>
    /// Pre-seeds a script variable. The value shows up in the debugger's variables view and the
    /// script can read or reassign it.
    /// </summary>
    public FishboneConfiguration AddValue(string name, object value)
    {
        Values[name] = value;
        return this;
    }

    /// <summary>
    /// Registers a conversion for a .NET type the generic interop path cannot handle (a type that is
    /// neither <see cref="IConvertible"/> nor an enum). <paramref name="toNet"/> turns a script value
    /// into <paramref name="netType"/> wherever one is expected (by-value, <c>ref</c>, or <c>out</c>
    /// arguments); the optional <paramref name="fromNet"/> normalizes a value of that type back into a
    /// script value when it returns from a call or is written back through <c>out</c>/<c>ref</c>. Omit
    /// <paramref name="fromNet"/> to leave instances of the type as opaque .NET objects.
    /// </summary>
    public FishboneConfiguration AddTypeConverter(Type netType,
                                                  Func<object, object> toNet,
                                                  Func<object, object>? fromNet = null)
    {
        TypeConverters[netType] = new FishboneTypeConverter(toNet, fromNet);
        return this;
    }

    /// <summary>
    /// Registers a .NET type so scripts can construct it by calling its name like a function.
    /// The script-visible name defaults to the type's name; pass <paramref name="name"/>
    /// to override it.
    /// </summary>
    public FishboneConfiguration AddType<T>(string? name = null)
    {
        return AddType(typeof(T), name);
    }

    /// <summary>
    /// Registers a .NET type so scripts can construct it by calling its name like a function.
    /// The script-visible name defaults to the type's name; pass <paramref name="name"/>
    /// to override it.
    /// </summary>
    public FishboneConfiguration AddType(Type type, string? name = null)
    {
        BuiltIns[name ?? type.Name] = new RegisteredType(type);
        return this;
    }

    /// <summary>
    /// Creates an independent copy of a Fishbone config.
    /// </summary>
    public FishboneConfiguration Clone()
    {
        var clone = new FishboneConfiguration()
        {
            EnableMemberAccess = EnableMemberAccess
        };
        foreach (var builtIn in BuiltIns)
            clone.BuiltIns[builtIn.Key] = builtIn.Value;
        foreach (var val in Values)
            clone.Values[val.Key] = val.Value;
        foreach (var converter in TypeConverters)
            clone.TypeConverters[converter.Key] = converter.Value;
        return clone;
    }
}