namespace Fishbone.Interpreter;

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