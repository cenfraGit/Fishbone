// --------------------------------------------------------------------------------
// ReservedTypes.cs
//
// lists the fishbone reserved primitive types.
// --------------------------------------------------------------------------------

namespace Fishbone;

public static class ReservedTypes
{
    private static readonly Dictionary<string, Type> _primitiveTypeNames = new(StringComparer.Ordinal)
    {
        ["sbyte"] = typeof(sbyte),
        ["byte"] = typeof(byte),
        ["short"] = typeof(short),
        ["ushort"] = typeof(ushort),
        ["int"] = typeof(int),
        ["uint"] = typeof(uint),
        ["long"] = typeof(long),
        ["ulong"] = typeof(ulong),
        ["float"] = typeof(float),
        ["double"] = typeof(double),
        ["decimal"] = typeof(decimal),
        ["char"] = typeof(char),
        ["string"] = typeof(string),
        ["bool"] = typeof(bool),
        ["object"] = typeof(object),
    };

    public static IReadOnlyDictionary<string, Type> PrimitiveTypeNames { get => _primitiveTypeNames; }

    // rejects reserved names, from scripts or from the host
    public static void ThrowIfReserved(string? name)
    {
        if (name is null) return;
        if (_primitiveTypeNames.ContainsKey(name))
            throw new ArgumentException($"Name '{name}' is reserved.");
    }
}