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

public partial class VariableExplorerVM : Tool, IRecipient<MessageExecutionFinished>, IRecipient<MessageDebugPaused>
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
        WeakReferenceMessenger.Default.Register<MessageExecutionFinished>(this);
        WeakReferenceMessenger.Default.Register<MessageDebugPaused>(this);
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

    public void Receive(MessageDebugPaused m)
    {
        Title = $"Variable Explorer ({m.Snapshot.Location.SourceId})";
        Variables.Clear();
        foreach (var variable in m.Snapshot.VisibleVariables)
        {
            if (variable.Value is Delegate)
                continue;

            Variables.Add(new VariableItem
            {
                Name = variable.Name,
                Type = VariableDisplayFormatter.FormatType(variable.Value),
                ValueDisplay = VariableDisplayFormatter.FormatValue(variable.Value),
                ValueRaw = variable.Value
            });
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