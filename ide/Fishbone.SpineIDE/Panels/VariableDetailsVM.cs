using System.Collections.ObjectModel;

namespace SpineIDE.Panels;

public class VariableDetailsVM
{
    public string Title { get; }
    public string Summary { get; }
    public ObservableCollection<VariableDetailItem> Rows { get; }

    public VariableDetailsVM(string name, object? value)
    {
        string variableName = string.IsNullOrWhiteSpace(name) ? "value" : name;
        Title = $"Variable Details - {variableName}";
        Summary = $"{variableName} : {VariableDisplayFormatter.FormatType(value)}";
        Rows = new ObservableCollection<VariableDetailItem>(VariableDisplayFormatter.BuildDetailRows(variableName, value));
    }
}