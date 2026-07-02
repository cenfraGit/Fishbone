using Dock.Model.Mvvm.Controls;
using System;
using System.Collections.ObjectModel;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fishbone.DebugClient;
using SpineIDE.Models.Messages;

namespace SpineIDE.Panels;

public partial class VariableItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ValueDisplay { get; set; } = string.Empty;
    public object? ValueRaw { get; set; }
    public ObservableCollection<VariableItem> Children { get; } = [];
    public FishboneVariableHandle? ChildrenHandle { get; init; }
    public IFishboneDebugClientSession? Session { get; init; }

    /// <summary>
    /// A stand-in child that makes the native tree expander chevron appear for a lazily-loaded node
    /// before its real children are fetched; it is replaced by the real children on first expand.
    /// </summary>
    public bool IsPlaceholder { get; init; }

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _canLoadChildren = true;

    private bool _childrenLoaded;
    private bool _loadingChildren;

    // The native expander toggles IsExpanded (TwoWay-bound in XAML); load lazy children when it opens.
    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            _ = EnsureChildrenLoadedAsync();
    }

    private async Task EnsureChildrenLoadedAsync()
    {
        if (_childrenLoaded || _loadingChildren)
            return;
        if (ChildrenHandle is null || Session is null || !CanLoadChildren)
            return;

        _loadingChildren = true;
        IsLoading = true;
        try
        {
            var loaded = await Session.GetVariablesAsync(ChildrenHandle);
            Children.Clear(); // remove the placeholder now that the real children are available
            foreach (FishboneDebugVariable child in loaded)
                Children.Add(FromDebugVariable(child, Session));
            _childrenLoaded = true;
        }
        catch (InvalidOperationException)
        {
            CanLoadChildren = false;
        }
        finally
        {
            _loadingChildren = false;
            IsLoading = false;
        }
    }

    public static VariableItem FromDebugVariable(FishboneDebugVariable variable, IFishboneDebugClientSession session)
    {
        var item = new VariableItem
        {
            Name = variable.Name,
            Type = variable.Type ?? string.Empty,
            ValueDisplay = variable.Value,
            ChildrenHandle = variable.ChildrenHandle,
            Session = session
        };
        // seed a placeholder so the native expander shows for a not-yet-loaded lazy node
        if (variable.ChildrenHandle is not null)
            item.Children.Add(new VariableItem { IsPlaceholder = true });
        return item;
    }

    public void InvalidateUnloadedHandles()
    {
        if (ChildrenHandle is not null && !_childrenLoaded)
            CanLoadChildren = false;
        foreach (VariableItem child in Children)
            child.InvalidateUnloadedHandles();
    }
}

public partial class VariableExplorerVM : Tool, IRecipient<MessageExecutionFinished>, IRecipient<MessageDebugPaused>, IRecipient<MessageDebugContinued>
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
        WeakReferenceMessenger.Default.Register<MessageDebugContinued>(this);
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

            var item = new VariableItem { Name = variableName, Type = variableType, ValueDisplay = variableValue, ValueRaw = v.Value };
            PopulateLocalChildren(item, v.Value);
            Variables.Add(item);
        }
    }

    public void Receive(MessageDebugPaused m)
    {
        string sourceName = m.Snapshot.Frames.FirstOrDefault()?.SourcePath ?? "Fishbone Script";
        Title = $"Variable Explorer ({Path.GetFileName(sourceName)})";
        Variables.Clear();
        FishboneDebugFrame? topFrame = m.Snapshot.Frames.FirstOrDefault();
        if (topFrame is null)
            return;
        foreach (FishboneDebugScope scope in topFrame.Scopes)
        {
            var scopeItem = new VariableItem
            {
                Name = scope.Name,
                Type = "Scope",
                ValueDisplay = $"{scope.Variables.Length} variables",
                Session = m.Session,
                IsExpanded = true
            };
            foreach (FishboneDebugVariable variable in scope.Variables)
                scopeItem.Children.Add(VariableItem.FromDebugVariable(variable, m.Session));
            Variables.Add(scopeItem);
        }
    }

    public void Receive(MessageDebugContinued m)
    {
        foreach (VariableItem variable in Variables)
            variable.InvalidateUnloadedHandles();
    }

    private static void PopulateLocalChildren(VariableItem parent, object? value)
    {
        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var child = new VariableItem { Name = $"[{VariableDisplayFormatter.FormatValue(entry.Key)}]", Type = VariableDisplayFormatter.FormatType(entry.Value), ValueDisplay = VariableDisplayFormatter.FormatValue(entry.Value), ValueRaw = entry.Value };
                PopulateLocalChildren(child, entry.Value);
                parent.Children.Add(child);
            }
        }
        else if (value is IEnumerable enumerable and not string)
        {
            int index = 0;
            foreach (object? element in enumerable)
            {
                var child = new VariableItem { Name = $"[{index++}]", Type = VariableDisplayFormatter.FormatType(element), ValueDisplay = VariableDisplayFormatter.FormatValue(element), ValueRaw = element };
                PopulateLocalChildren(child, element);
                parent.Children.Add(child);
            }
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