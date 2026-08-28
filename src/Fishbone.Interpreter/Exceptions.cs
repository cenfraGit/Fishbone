// --------------------------------------------------------------------------------
// Exceptions.cs
//
// this file holds exception types that are used in control flow during execution.
// these are implemented as exceptions just to break control flow easily.
// --------------------------------------------------------------------------------

using Fishbone.Core;

namespace Fishbone.Interpreter;

// thrown when a "return" is hit (from fb functions)
internal class ReturnException(object? value) : Exception
{
    public object? Value { get; } = value;
}

// thrown when a "break" is hit
internal class BreakException : Exception { }

// thrown when a "continue" is hit
internal class ContinueException : Exception { }

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