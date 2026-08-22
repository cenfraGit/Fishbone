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
using Fishbone.Core;
using Fishbone.DebugClient;

namespace SpineIDE.Views.Editor;

public partial class ScriptEditorVM : Document
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

    // tracks unsaved edits so closing a tab can prompt to save; reset on save, set on any text edit
    [ObservableProperty] private bool _isDirty;
    private TextDocument? _trackedDocument;
    public string SourceId { get; }
    public bool IsRemote { get; }
    public bool IsReadOnly => IsDebugging || IsRemote;
    public bool CanToggleBreakpoints => !IsRemote || IsDebugging;
    public event EventHandler? BreakpointsChanged;
    public event EventHandler? BreakpointVisualsChanged;
    private readonly Dictionary<int, FishboneBreakpointResult> _breakpointResults = [];

    // two buckets with different lifetimes: syntax diagnostics are replaced on every typing
    // pause, run diagnostics survive until the next edit or run. kept apart so neither clears the
    // other, and resolved into DiagnosticSegments once per change rather than once per paint
    private IReadOnlyList<FishboneDiagnostic> _syntaxDiagnostics = [];
    private IReadOnlyList<FishboneDiagnostic> _runDiagnostics = [];
    private IReadOnlyList<DiagnosticSegment> _diagnosticSegments = [];

    /// <summary>Raised whenever the underlines need repainting.</summary>
    public event EventHandler? DiagnosticsChanged;

    internal IReadOnlyList<DiagnosticSegment> DiagnosticSegments => _diagnosticSegments;

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

        WeakReferenceMessenger.Default.Register<MessageDebugEditingChanged>(this, (recipient, message) =>
        {
            if (message.SourceId == SourceId)
                IsDebugging = message.IsDebugging;
        });

        WeakReferenceMessenger.Default.Register<MessageDiagnostics>(this, (recipient, message) =>
        {
            if (message.SourceId == SourceId)
                SetRunDiagnostics(message.Diagnostics);
        });
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    partial void OnScriptDocumentChanged(TextDocument value)
    {
        if (_trackedDocument is not null)
            _trackedDocument.TextChanged -= OnDocumentTextChanged;
        _trackedDocument = value;
        value.TextChanged += OnDocumentTextChanged;

        // offsets from the previous document mean nothing in this one
        _runDiagnostics = [];
        RebuildDiagnosticSegments();
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        IsDirty = true;

        // a run's errors describe a program that no longer exists, so the first edit after a run
        // retires them rather than letting the marks drift onto whatever now sits at that offset.
        // guarded by the emptiness check so typing costs nothing once they are gone
        if (_runDiagnostics.Count > 0)
        {
            _runDiagnostics = [];
            RebuildDiagnosticSegments();
        }
    }

    // --------------------------------------------------------------------------------
    // diagnostics
    // --------------------------------------------------------------------------------

    /// <summary>
    /// Replaces everything the last run of this script reported, whether it failed to parse or
    /// failed while executing. Cleared by the next edit and by the next run.
    /// </summary>
    public void SetRunDiagnostics(IReadOnlyList<FishboneDiagnostic> diagnostics)
    {
        // deliberately not filtered by stage. a run is the only source of diagnostics until live
        // parsing exists, so dropping the syntax ones here would leave a syntax error with no mark
        // at all. double-marking is prevented by the suppression rule in RebuildDiagnosticSegments
        // rather than by filtering, so this stays correct once live parsing does arrive
        _runDiagnostics = diagnostics;
        RebuildDiagnosticSegments();
    }

    /// <summary>Drops the diagnostics from the last run, without touching syntax diagnostics.</summary>
    public void ClearRunDiagnostics()
    {
        if (_runDiagnostics.Count == 0)
            return;

        _runDiagnostics = [];
        RebuildDiagnosticSegments();
    }

    /// <summary>Replaces the syntax diagnostics, which a live parse produces on every typing pause.</summary>
    public void SetSyntaxDiagnostics(IReadOnlyList<FishboneDiagnostic> diagnostics)
    {
        _syntaxDiagnostics = diagnostics;
        RebuildDiagnosticSegments();
    }

    // resolves both buckets against the current document once, so the renderer does no offset
    // arithmetic per paint
    private void RebuildDiagnosticSegments()
    {
        var segments = new List<DiagnosticSegment>();

        Add(_syntaxDiagnostics);

        // a file that no longer parses makes the last run's errors stale by definition, so the
        // syntax bucket suppresses the run bucket rather than sitting alongside it. this is also
        // what stops a run's syntax errors being marked twice once live parsing finds them too
        if (_syntaxDiagnostics.Count == 0)
            Add(_runDiagnostics);

        _diagnosticSegments = segments;

        void Add(IReadOnlyList<FishboneDiagnostic> diagnostics)
        {
            foreach (FishboneDiagnostic diagnostic in diagnostics)
                if (DiagnosticSpans.TryResolve(ScriptDocument, diagnostic.Span, out int start, out int length))
                    segments.Add(new DiagnosticSegment(start, length, diagnostic.Severity));
        }
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
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