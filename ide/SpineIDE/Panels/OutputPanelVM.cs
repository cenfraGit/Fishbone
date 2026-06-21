using System;
using System.Text;

namespace SpineIDE.Panels;

public class OutputPanelVM : TextPanelVM
{
    private readonly StringBuilder _outputBuilder = new();

    public void Clear()
    {
        _outputBuilder.Clear();
        PanelText = string.Empty;
    }

    public void Append(object? value)
    {
        AppendBatch(value?.ToString() ?? string.Empty);
    }

    public void AppendLine(object? value)
    {
        AppendBatch((value?.ToString() ?? string.Empty) + Environment.NewLine);
    }

    public void AppendBatch(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _outputBuilder.Append(text);
        PanelText = _outputBuilder.ToString();
    }
}