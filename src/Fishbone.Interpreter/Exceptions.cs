namespace Fishbone.Interpreter;

public class ReturnException(object values) : Exception
{
    public object Values { get; } = values;
}

public class BreakException : Exception { }
public class ContinueException : Exception { }

/// <summary>
/// Thrown by a script <c>throw</c> statement when the thrown value is not already a
/// .NET <see cref="Exception"/>. The original value is preserved in <see cref="Value"/>.
/// </summary>
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