using System.Collections.ObjectModel;

namespace SpineIDE.Services;

public class ScriptExecutionError
{
    public string? Line { get; set; }
    public string? Column { get; set; }
    public string ExMessage { get; set; }

    public ScriptExecutionError(string message, string? line = null, string? column = null)
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