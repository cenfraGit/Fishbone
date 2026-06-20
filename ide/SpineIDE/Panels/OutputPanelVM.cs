using Dock.Model.Mvvm.Controls;
using System;

namespace SpineIDE.Panels;

public class OutputPanelVM : Tool
{
    private string _outputText = string.Empty;

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public void Clear()
    {
        OutputText = string.Empty;
    }

    public void Append(object? value)
    {
        OutputText += value?.ToString() ?? string.Empty;
    }

    public void AppendLine(object? value)
    {
        OutputText += (value?.ToString() ?? string.Empty) + Environment.NewLine;
    }
}