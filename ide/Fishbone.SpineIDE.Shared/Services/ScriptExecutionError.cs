using Fishbone;

namespace SpineIDE.Services;

public sealed record ScriptExecutionError(string ExMessage, int? Line = null, int? Column = null)
{
    public bool HasLocation => Line is not null;
    public string LocationDisplay => Column is null ? $"Line {Line}" : $"Line {Line}, column {Column}";

    /// <summary>One error per parse error, or the exception's message with its location if it has one.</summary>
    public static IReadOnlyList<ScriptExecutionError> From(Exception exception)
    {
        if (exception is FishboneParseException parseException)
            return parseException.Errors
                .Select(error => new ScriptExecutionError(error.Message, error.Line > 0 ? error.Line : null, error.Column > 0 ? error.Column : null))
                .ToArray();

        if (exception is FishboneRuntimeException runtimeException)
            return [new ScriptExecutionError(
                exception.Message,
                runtimeException.Line > 0 ? runtimeException.Line : null,
                runtimeException.Column > 0 ? runtimeException.Column : null)];

        return [new ScriptExecutionError(exception.Message)];
    }
}
