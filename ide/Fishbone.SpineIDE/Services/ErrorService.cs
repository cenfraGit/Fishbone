using System.Collections.ObjectModel;

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