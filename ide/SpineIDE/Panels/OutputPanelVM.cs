using Dock.Model.Mvvm.Controls;
using System;
using System.Text;

namespace SpineIDE.Panels;

public class OutputPanelVM : Tool
{
    private readonly StringBuilder _outputBuilder = new();
    private string _outputText = string.Empty;

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public void Clear()
    {
        _outputBuilder.Clear();
        OutputText = string.Empty;
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
        OutputText = _outputBuilder.ToString();
    }
}