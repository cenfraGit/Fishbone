// --------------------------------------------------------------------------------
// FishboneConfiguration.cs
//
// a configuration object used to set up a fishbone execution environment.
// --------------------------------------------------------------------------------

using Fishbone.Interpreter;
using System.Globalization;

namespace Fishbone.Engine;

public class FishboneConfiguration
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    /// <summary>
    /// When false, scripts cannot use the <c>.</c> operator (no property/field/method
    /// reads or calls). Mostly safety mechanism for simple scripts.
    /// </summary>
    public bool EnableMemberAccess { get; set; } = true;

    /// <summary>
    /// Ambient names available to every script (functions, types, constants). Cannot
    /// be overwritten, although can be shadowed. Not shown in debugger's variable view.
    /// </summary>
    public Dictionary<string, object> BuiltIns { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Pre-seeded script variables. These do appear in the debugger's variable view,
    /// and may be used/mutated at runtime.
    /// </summary>
    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Host-registered conversions between script values and .NET types the generic interop path
    /// cannot convert on its own (anything not <see cref="IConvertible"/> or an enum).
    /// </summary>
    public Dictionary<Type, FishboneTypeConverter> TypeConverters { get; } = [];

    // --------------------------------------------------------------------------------
    // constructors
    // --------------------------------------------------------------------------------

    public FishboneConfiguration(bool injectDefaults = true)
    {
        if (injectDefaults)
            AddDefaults();
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
    public FishboneConfiguration AddTypeConverter(
        Type netType,
        Func<object, object> toNet,
        Func<object, object>? fromNet = null)
    {
        TypeConverters[netType] = new FishboneTypeConverter(toNet, fromNet);
        return this;
    }

    /// <summary>
    /// Registers a .NET type so scripts can construct it by calling its name like a function,
    /// for example <c>let p = Point(1, 2);</c>. The script-visible name defaults to the type's
    /// short name; pass <paramref name="name"/> to override it.
    /// </summary>
    public FishboneConfiguration AddType<T>(string? name = null) =>
        AddType(typeof(T), name);

    public FishboneConfiguration AddType(Type type, string? name = null)
    {
        BuiltIns[name ?? type.Name] = new RegisteredType(type);
        return this;
    }

    /// <summary>
    /// Creates an independent copy carrying all ambient state (builtins, preseeded values, and
    /// type converters).
    /// </summary>
    public FishboneConfiguration Clone()
    {
        var clone = new FishboneConfiguration(injectDefaults: false)
        {
            EnableMemberAccess = EnableMemberAccess
        };
        foreach (var builtIn in BuiltIns)
            clone.BuiltIns[builtIn.Key] = builtIn.Value;
        foreach (var value in Values)
            clone.Values[value.Key] = value.Value;
        foreach (var converter in TypeConverters)
            clone.TypeConverters[converter.Key] = converter.Value;
        return clone;
    }

    // --------------------------------------------------------------------------------
    // standard functions/constants
    // --------------------------------------------------------------------------------

    private void AddDefaults()
    {
        AddDefaultConstants();
        AddDefaultIO();
        AddDefaultMath();
        AddDefaultReflection();
    }

    private void AddDefaultConstants()
    {
        BuiltIns["PI"] = Math.PI;
        BuiltIns["E"] = Math.E;
    }

    private void AddDefaultIO()
    {
        BuiltIns["print"] = new Action<object?>(value => Console.Write(value?.ToString()));
        BuiltIns["println"] = new Action<object?>(value => Console.WriteLine(value?.ToString()));
        BuiltIns["input"] = new Func<string>(() => Console.ReadLine() ?? string.Empty);
    }

    private void AddDefaultMath()
    {
        BuiltIns["abs"] = new Func<double, double>(Math.Abs);
        BuiltIns["round"] = new Func<double, int, double>(Math.Round);
        BuiltIns["min"] = new Func<double, double, double>(Math.Min);
        BuiltIns["max"] = new Func<double, double, double>(Math.Max);
        BuiltIns["pow"] = new Func<double, double, double>(Math.Pow);
        BuiltIns["sqrt"] = new Func<double, double>(Math.Sqrt);
    }

    private void AddDefaultReflection()
    {
        // conversions parse and format with the invariant culture so scripts behave the
        // same regardless of the host machine's locale
        BuiltIns["int"] = new Func<object?, int>(value =>
        {
            if (value is null) return 0;
            if (value is string str)
            {
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double strDouble))
                    return (int)strDouble;
                return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int strInt) ? strInt : 0;
            }
            try
            {
                return (int)Convert.ChangeType(value, typeof(int), CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        });

        BuiltIns["double"] = new Func<object?, double>(value =>
        {
            if (value is null) return 0.0;
            if (value is string str)
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double strDouble))
                    return strDouble;

            try
            {
                return (double)Convert.ChangeType(value, typeof(double), CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0.0;
            }
        });

        BuiltIns["string"] = new Func<object?, string>(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
    }
}