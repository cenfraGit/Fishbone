using Fishbone.Interpreter;

namespace Fishbone.Engine;

public class FishboneConfiguration
{
    public bool EnableImports { get; set; } = true;
    public bool EnableLoops { get; set; } = true;
    public bool EnableFunctionDeclaration { get; set; } = true;
    public bool EnableFunctionCall { get; set; } = true;

    /// <summary>
    /// Ambient names available to every script — functions, types, and constants. These are not
    /// shown in the debugger's variables view.
    /// </summary>
    public Dictionary<string, object> BuiltIns { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Pre-seeded script variables. Unlike built-ins, these appear in the debugger's variables
    /// view and behave like ordinary top-level variables (readable and assignable).
    /// </summary>
    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Host-registered conversions between script values and .NET types the generic interop path
    /// cannot convert on its own (anything not <see cref="IConvertible"/> or an enum), keyed by the
    /// .NET type. See <see cref="AddTypeConverter(Type, Func{object, object}, Func{object, object}?)"/>.
    /// </summary>
    public Dictionary<Type, FishboneTypeConverter> TypeConverters { get; } = [];

    public FishboneConfiguration(bool injectDefaults = true)
    {
        if (injectDefaults)
            AddDefaults();
    }

    /// <summary>Binds an ambient built-in (function, value, or registered type) under a name.</summary>
    public FishboneConfiguration AddBuiltIn(string name, object value)
    {
        BuiltIns[name] = value;
        return this;
    }

    /// <summary>
    /// Pre-seeds a script variable. The value shows up in the debugger's variables view and the
    /// script can read or reassign it. Use this for injected data, as opposed to ambient API.
    /// </summary>
    public FishboneConfiguration AddValue(string name, object value)
    {
        Values[name] = value;
        return this;
    }

    /// <summary>Exposes a .NET delegate as a callable function.</summary>
    public FishboneConfiguration AddFunction(string name, Delegate csharpMethod)
    {
        BuiltIns[name] = csharpMethod;
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
    /// Creates an independent copy carrying all ambient state (builtins, preseeded values, type
    /// converters, and the capability flags).
    /// </summary>
    public FishboneConfiguration Clone()
    {
        var clone = new FishboneConfiguration(injectDefaults: false)
        {
            EnableImports = EnableImports,
            EnableLoops = EnableLoops,
            EnableFunctionDeclaration = EnableFunctionDeclaration,
            EnableFunctionCall = EnableFunctionCall
        };
        foreach (var builtIn in BuiltIns)
            clone.BuiltIns[builtIn.Key] = builtIn.Value;
        foreach (var value in Values)
            clone.Values[value.Key] = value.Value;
        foreach (var converter in TypeConverters)
            clone.TypeConverters[converter.Key] = converter.Value;
        return clone;
    }

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
        BuiltIns["int"] = new Func<object?, int>(value =>
        {
            if (value is null) return 0;
            if (value is string str)
            {
                if (double.TryParse(str, out double strDouble))
                    return (int)strDouble;
                return int.TryParse(str, out int strInt) ? strInt : 0;
            }
            try
            {
                return (int)Convert.ChangeType(value, typeof(int));
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
                if (double.TryParse(str, out double strDouble))
                    return strDouble;

            try
            {
                return (double)Convert.ChangeType(value, typeof(double));
            }
            catch
            {
                return 0.0;
            }
        });

        BuiltIns["string"] = new Func<object?, string>(value => value?.ToString() ?? string.Empty);
    }
}
