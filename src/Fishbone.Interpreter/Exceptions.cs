// --------------------------------------------------------------------------------
// Exceptions.cs
//
// this file holds exception types that are used in control flow during execution.
// these are implemented as exceptions just to break control flow easily.
// --------------------------------------------------------------------------------

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
public sealed class FishboneScriptException : Exception
{
    public object? Value { get; }

    public FishboneScriptException(object? value)
        : base(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) is { Length: > 0 } text
            ? text
            : "A value was thrown by the script.")
    {
        Value = value;
    }
}