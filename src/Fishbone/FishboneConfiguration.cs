// --------------------------------------------------------------------------------
// FishboneConfiguration.cs
//
// a configuration object used to set up a fishbone execution environment.
// --------------------------------------------------------------------------------

using Fishbone.Debugging;
using Fishbone.Interpreter;

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
    /// Cannot be overwritten (but can be shadowed by Value).
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

    /// <summary>
    /// Host-registered ways to show a .NET value as an image in the debugger, keyed by the type
    /// they were registered for.
    /// </summary>
    public Dictionary<Type, FishboneVisualizer> Visualizers { get; } = [];

    // --------------------------------------------------------------------------------
    // constructors
    // --------------------------------------------------------------------------------

    public FishboneConfiguration()
    {
    }

    // --------------------------------------------------------------------------------
    // setup methods
    // --------------------------------------------------------------------------------

    /// <summary>
    /// Lets the debugger show values of <typeparamref name="T"/> (and types derived from it) as
    /// images. <paramref name="toImage"/> runs in the host while the script is paused, and returns
    /// null when there's nothing to show. <paramref name="canShow"/> is a cheap check for whether a
    /// value is an image at all; without it, every <typeparamref name="T"/> counts as one.
    /// </summary>
    public FishboneConfiguration AddVisualizer<T>(Func<T, FishboneImage?> toImage, Func<T, bool>? canShow = null)
    {
        Visualizers[typeof(T)] = new FishboneVisualizer(
            value => toImage((T)value),
            canShow is null ? null : value => canShow((T)value));
        return this;
    }

    /// <summary>Whether the debugger can show this value as an image.</summary>
    public bool CanVisualize(object? value) =>
        value is not null && FindVisualizer(value) is { } visualizer && (visualizer.CanShow?.Invoke(value) ?? true);

    /// <summary>The value as an image, or null when no visualizer applies or it has nothing to show.</summary>
    public FishboneImage? Visualize(object? value) =>
        CanVisualize(value) ? FindVisualizer(value!)!.ToImage(value!) : null;

    // the visualizer for the value's own type, or else the first one registered for a base type
    private FishboneVisualizer? FindVisualizer(object value) =>
        Visualizers.TryGetValue(value.GetType(), out var exact)
            ? exact
            : Visualizers.FirstOrDefault(entry => entry.Key.IsInstanceOfType(value)).Value;

    /// <summary>
    /// Describes every name this configuration puts into scripts, and the members scripts can reach
    /// on them. For tools like completion and the static analyzer.
    /// </summary>
    public FishboneDescription Describe() => new(this);

    /// <summary>Binds an ambient built-in (function, value, or registered type) under a name.</summary>
    public FishboneConfiguration AddBuiltIn(string name, object value)
    {
        ReservedTypes.ThrowIfReserved(name);
        BuiltIns[name] = value;
        return this;
    }

    /// <summary>
    /// Pre-seeds a script variable. The value shows up in the debugger's variables view and the
    /// script can read or reassign it.
    /// </summary>
    public FishboneConfiguration AddValue(string name, object value)
    {
        ReservedTypes.ThrowIfReserved(name);
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
        name ??= type.Name;
        ReservedTypes.ThrowIfReserved(name);
        BuiltIns[name] = new RegisteredType(type);
        return this;
    }

    /// <summary>
    /// Loads the contents of an IFishbonePlugin into this FishboneConfiguration.
    /// </summary>
    public FishboneConfiguration AddPlugin(IFishbonePlugin plugin)
    {
        // by "loading" we mean that this FishboneConfiguration (us)
        // will have access to whatever the plugin adds to our built-ins,
        // values, type converters, etc.
        plugin.Register(this);
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
        foreach (var visualizer in Visualizers)
            clone.Visualizers[visualizer.Key] = visualizer.Value;
        return clone;
    }
}