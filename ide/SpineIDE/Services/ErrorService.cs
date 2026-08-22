using System.Collections.ObjectModel;
using Fishbone.Core;

namespace SpineIDE.Services;

/// <summary>
/// One error as the Errors panel shows it. Carries the whole <see cref="FishboneDiagnostic"/>
/// rather than a flattened line/column pair, so the span survives as far as the editor, which
/// needs the end position to size an underline.
/// </summary>
public class ScriptExecutionError
{
    /// <summary>The underlying diagnostic, when the error came from one. Null for a bare message.</summary>
    public FishboneDiagnostic? Diagnostic { get; }

    /// <summary>The script this error belongs to, or null when it is not attributable to one.</summary>
    public string? SourceId { get; }

    public string ExMessage { get; }

    public int? Line { get; }
    public int? Column { get; }

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

    public ScriptExecutionError(FishboneDiagnostic diagnostic, string? sourceId = null)
    {
        this.Diagnostic = diagnostic;
        this.SourceId = sourceId;
        this.ExMessage = diagnostic.Message;
        // an unknown span reads as no location at all, which is what the panel already expects
        this.Line = diagnostic.Span.IsKnown ? diagnostic.Span.Line : null;
        this.Column = diagnostic.Span.IsKnown ? diagnostic.Span.Column : null;
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