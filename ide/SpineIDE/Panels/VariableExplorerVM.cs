using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpineIDE.Models.Messages;

namespace SpineIDE.Panels;

public class VariableItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ValueDisplay { get; set; } = string.Empty;
    public object? ValueRaw { get; set; }
}

public partial class VariableExplorerVM : Tool, IRecipient<MessageExecutionFinished>
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    public ObservableCollection<VariableItem> Variables { get; set; } = [];

    private VariableItem? _selectedVariable;
    public VariableItem? SelectedVariable
    {
        get => _selectedVariable;
        set
        {
            SetProperty(ref _selectedVariable, value);
        }
    }

    // --------------------------------------------------------------------------------
    // constructor
    // --------------------------------------------------------------------------------

    public VariableExplorerVM()
    {
        WeakReferenceMessenger.Default.Register(this);
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    public void Receive(MessageExecutionFinished m)
    {
        this.Title = $"Variable Explorer ({m.ScriptName})";
        Variables.Clear();
        foreach (var v in m.Environment.Values)
        {
            if (v.Value is Delegate)
                continue;

            string variableName = v.Key;
            string variableType = VariableDisplayFormatter.FormatType(v.Value);
            string variableValue = VariableDisplayFormatter.FormatValue(v.Value);

            Variables.Add(new VariableItem { Name = variableName,
                                             Type = variableType,
                                             ValueDisplay = variableValue,
                                             ValueRaw = v.Value });
        }
    }

    [RelayCommand]
    private void OpenVariableDetails(VariableItem? variable)
    {
        if (variable is null || !VariableDisplayFormatter.IsCollection(variable.ValueRaw))
            return;

        WeakReferenceMessenger.Default.Send(new MessageVariableDetailsRequested(variable.Name, variable.ValueRaw));
    }
}