// --------------------------------------------------------------------------------
// FishboneTypeConverter.cs
//
// even though fishbone itself works with .net types, some .net types
// cannot be converted automatically. the normal conversion path only
// knows how to handle IConvertible types (numbers, strings, bool),
// and enums. anything else has no direct way of converting a script
// value into it (like a class or struct)
//
// so in cases like OpenCV's Size or Point, "[640, 480]" is just a
// list. here's where FishboneTypeConverter may be used (in a plugin
// for example) so the host can register, for a specific type, how to
// convert to .net (a script value into that .net type) and from .net
// (the .net type itself into a script value, used for out or return
// values coming back).
// --------------------------------------------------------------------------------

namespace Fishbone;

/// <summary>
/// A host-registered conversion between a script value and a .NET type that the generic interop
/// path (which only understands <see cref="IConvertible"/> and enums) cannot convert on its own.
/// Registered through <c>FishboneConfiguration.AddTypeConverter</c> and consulted at the interop
/// boundary: <see cref="ToNet"/> when a script value must satisfy a parameter of the registered
/// type, and <see cref="FromNet"/> when a value of that type crosses back into the script (a method
/// return value or an <c>out</c>/<c>ref</c> write-back).
/// </summary>
public sealed class FishboneTypeConverter
{
    public FishboneTypeConverter(Func<object, object> toNet, Func<object, object>? fromNet = null)
    {
        ToNet = toNet ?? throw new ArgumentNullException(nameof(toNet));
        FromNet = fromNet;
    }

    /// <summary>Converts a script value into the registered .NET type.</summary>
    public Func<object, object> ToNet { get; }

    /// <summary>
    /// Converts a value of the registered .NET type back into a script value, or null to leave such
    /// values as-is (kept as opaque .NET objects the script can still interop with).
    /// </summary>
    public Func<object, object>? FromNet { get; }
}