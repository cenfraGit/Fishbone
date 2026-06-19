using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.ObjectModel;
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
            if (SetProperty(ref _selectedVariable, value))
                if (_selectedVariable is not null)
                    WeakReferenceMessenger.Default.Send(new MessageVariableSelected(_selectedVariable.Name, _selectedVariable.ValueRaw));
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

    public async void Receive(MessageExecutionFinished m)
    {
        this.Title = $"Variable Explorer ({m.ScriptName})";
        Variables.Clear();
        foreach (var v in m.Environment.Values)
        {
            if (v.Value is Delegate)
                continue;

            string variableName = v.Key;
            string variableType = v.Value?.GetType().Name ?? "null";
            string variableValue = v.Value?.ToString() ?? "null";

            // handle when matrix (generic)
            variableType = variableType.Contains("`")
                ? variableType.Substring(0, variableType.IndexOf('`'))
                : variableType;

            Variables.Add(new VariableItem { Name = variableName,
                                             Type = variableType,
                                             ValueDisplay = variableValue,
                                             ValueRaw = v.Value });
        }
    }
}