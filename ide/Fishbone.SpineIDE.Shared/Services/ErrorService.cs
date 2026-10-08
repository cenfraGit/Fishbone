using System.Collections.ObjectModel;
using Fishbone;

namespace SpineIDE.Services;

public class ScriptExecutionError
{
    public int? Line { get; set; }
    public int? Column { get; set; }
    public string ExMessage { get; set; }
    public bool HasLocation => Line is not null || Column is not null;
    public string LocationDisplay => (Line, Column) switch
    {
        (int line, int col) => $"Line {line}, column {col}",
        (int line, _) => $"Line {line}",
        (_, int col) => $"Column {col}",
        _ => string.Empty
    };

    public ScriptExecutionError(string message, int? line = null, int? column = null)
    {
        this.ExMessage = message;
        this.Line = line;
        this.Column = column;
    }

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

public interface IErrorService
{
    ObservableCollection<ScriptExecutionError> Errors { get; set; }

    void AddError(ScriptExecutionError ex);
    void ClearErrors();
}

public class ErrorService : IErrorService
{
    public ObservableCollection<ScriptExecutionError> Errors { get; set; } = [];

    public void AddError(ScriptExecutionError ex)
    {
        this.Errors.Add(ex);
    }

    public void ClearErrors()
    {
        this.Errors.Clear();
    }
}