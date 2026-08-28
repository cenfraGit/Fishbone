// --------------------------------------------------------------------------------
// FishboneScriptException.cs
//
// the exception a script's own 'throw' produces when the thrown value is not
// already a .NET exception.
// --------------------------------------------------------------------------------

namespace Fishbone;

// thrown by a script "throw" statement when the thrown value is not already
// a .NET exception. The original value is preserved in Value.
//
// derives from FishboneRuntimeException so a host can catch every script error with a
// single catch, and narrow to this type only when it cares that the script threw. it
// carries its own line/column, which is what stops the interpreter wrapping it again
public sealed class FishboneScriptException : FishboneRuntimeException
{
    public object? Value { get; }

    public FishboneScriptException(object? value, int line = 0, int column = 0)
        : base(DescribeValue(value), line, column)
    {
        Value = value;
    }

    private static string DescribeValue(object? value) =>
        Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) is { Length: > 0 } text
            ? text
            : "A value was thrown by the script.";
}