using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using SpineIDE.Models;
using SpineIDE.Models.Messages;
using Fishbone.DebugClient;

namespace SpineIDE.Views.Editor;

public partial class ScriptEditorVM : Document, IRecipient<MessageRunActiveScript>
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    private string _scriptNameTemp = string.Empty; // used if no path specified

    [ObservableProperty] TextDocument _scriptDocument;
    private readonly List<TextAnchor> _breakpoints = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    [NotifyPropertyChangedFor(nameof(CanToggleBreakpoints))]
    private bool _isDebugging;
    public string SourceId { get; }
    public bool IsRemote { get; }
    public bool IsReadOnly => IsDebugging || IsRemote;
    public bool CanToggleBreakpoints => !IsRemote || IsDebugging;
    public event EventHandler? BreakpointsChanged;
    public event EventHandler? BreakpointVisualsChanged;
    private readonly Dictionary<int, FishboneBreakpointResult> _breakpointResults = [];

    public string? ScriptPath { get; set; }
    public string ScriptName
    {
        get
        {
            if (this.ScriptPath is null) return _scriptNameTemp;
            return Path.GetFileName(this.ScriptPath);
        }
    }

    // --------------------------------------------------------------------------------
    // constructor
    // --------------------------------------------------------------------------------

    public ScriptEditorVM(
        string name,
        string? path,
        string contents,
        string? sourceId = null,
        bool isRemote = false)
    {
        this.Title = isRemote ? $"[Remote] {name}" : name;
        this._scriptNameTemp = name;
        this.ScriptPath = path;
        this.ScriptDocument = new(contents);
        SourceId = sourceId ?? Guid.NewGuid().ToString("N");
        IsRemote = isRemote;

        WeakReferenceMessenger.Default.Register(this);
        WeakReferenceMessenger.Default.Register<MessageDebugEditingChanged>(this, (recipient, message) =>
        {
            if (message.SourceId == SourceId)
                IsDebugging = message.IsDebugging;
        });
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    public async void Receive(MessageRunActiveScript m)
    {
        // whenever we receive a "run script" message, we'll broadcast back a message with the script data
        if (this.IsActive && !IsRemote)
        {
            var scriptData = new Script(this.ScriptName, this.ScriptPath, ScriptDocument.Text, SourceId);
            WeakReferenceMessenger.Default.Send(new MessageExecute(scriptData, m.Mode, BreakpointLines));
        }
    }

    public IReadOnlyList<int> BreakpointLines => _breakpoints
        .Where(anchor => !anchor.IsDeleted)
        .Select(anchor => ScriptDocument.GetLineByOffset(anchor.Offset).LineNumber)
        .Distinct()
        .OrderBy(line => line)
        .ToArray();

    public bool HasBreakpoint(int line) => BreakpointLines.Contains(line);
    public bool IsBreakpointVerified(int line) => !_breakpointResults.TryGetValue(line, out var result) || result.Verified;

    public void ApplyBreakpointResults(IReadOnlyList<FishboneBreakpointResult> results)
    {
        _breakpointResults.Clear();
        foreach (FishboneBreakpointResult result in results)
            _breakpointResults[result.Line] = result;
        BreakpointVisualsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleBreakpoint(int line)
    {
        if (!CanToggleBreakpoints)
            return;
        if (line < 1 || line > ScriptDocument.LineCount)
            return;

        var existing = _breakpoints.FirstOrDefault(anchor =>
            !anchor.IsDeleted && ScriptDocument.GetLineByOffset(anchor.Offset).LineNumber == line);
        if (existing is not null)
        {
            _breakpoints.Remove(existing);
            _breakpointResults.Remove(line);
            BreakpointsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var documentLine = ScriptDocument.GetLineByNumber(line);
        var anchor = ScriptDocument.CreateAnchor(documentLine.Offset);
        anchor.MovementType = AnchorMovementType.BeforeInsertion;
        anchor.SurviveDeletion = true;
        _breakpoints.Add(anchor);
        _breakpointResults.Remove(line);
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }
}