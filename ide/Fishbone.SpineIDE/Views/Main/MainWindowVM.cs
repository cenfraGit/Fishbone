using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Controls;
using Dock.Model.Core;
using Fishbone;
using Fishbone.DebugClient;
using SpineIDE.Models;
using SpineIDE.Models.Layout;
using SpineIDE.Models.Messages;
using SpineIDE.Panels;
using SpineIDE.Services;
using SpineIDE.Views.Editor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SaveConfirmationResult = SpineIDE.Models.SaveConfirmationResult;

namespace SpineIDE.Views.Main;

public partial class MainWindowVM : ObservableObject, IRecipient<MessageExecute>, IRecipient<MessageVariableDetailsRequested>
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    IDialogService _dialogService;
    private readonly OutputPanelVM _outputPanel;
    public IErrorService ErrorService { get; set; }

    private static int _newFileCounter = 1;

    private readonly ScriptSession _session;
    private ScriptEditorVM? _debugEditor;

    [ObservableProperty] private FishboneDebugSessionState _debugState = FishboneDebugSessionState.Completed;

    public ObservableCollection<MenuItemViewModel> FunctionMenuItems { get; } = new();
    [ObservableProperty] IFactory? _factory;
    [ObservableProperty] IRootDock? _layout;

    // toggled from the Views menu; drives the app-wide light/dark theme variant
    [ObservableProperty] private bool _isLightTheme;

    partial void OnIsLightThemeChanged(bool value)
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = value
                ? Avalonia.Styling.ThemeVariant.Light
                : Avalonia.Styling.ThemeVariant.Dark;
    }

    // Views-menu toggles for the dockable tools. They start visible to match the initial layout.
    [ObservableProperty] private bool _isVariableExplorerVisible = false;
    [ObservableProperty] private bool _isOutputVisible = false;
    [ObservableProperty] private bool _isErrorsVisible = false;

    // set while syncing a checkmark from a dock-driven close, so we don't loop back into the dock
    private bool _suppressVisibilitySync;

    private DockFactory? DockFactory => Factory as DockFactory;

    partial void OnIsVariableExplorerVisibleChanged(bool value) => ApplyToolVisibility(DockFactory?.VariableExplorer, value);
    partial void OnIsOutputVisibleChanged(bool value) => ApplyToolVisibility(DockFactory?.OutputPanel, value);
    partial void OnIsErrorsVisibleChanged(bool value) => ApplyToolVisibility(DockFactory?.ErrorPanel, value);

    // drives the dock from a checkmark change; guarded so a sync coming the other way doesn't loop
    private void ApplyToolVisibility(IDockable? tool, bool visible)
    {
        if (_suppressVisibilitySync || DockFactory is not { } f || tool is null)
            return;

        if (visible)
            f.ShowTool(tool);
        else
            f.HideTool(tool);
    }

    // keeps the Views-menu checkmarks in sync when a tool is shown/hidden by the dock itself
    // (e.g. via a tab's close button, which the factory routes through HideTool)
    private void OnToolVisibilityChanged(IDockable tool, bool visible)
    {
        if (DockFactory is not { } f)
            return;

        _suppressVisibilitySync = true;
        if (ReferenceEquals(tool, f.VariableExplorer)) IsVariableExplorerVisible = visible;
        else if (ReferenceEquals(tool, f.OutputPanel)) IsOutputVisible = visible;
        else if (ReferenceEquals(tool, f.ErrorPanel)) IsErrorsVisible = visible;
        _suppressVisibilitySync = false;
    }

    // brings the Errors tool to the front (re-showing and expanding it if needed) whenever an error
    // is reported, so failures are never hidden behind another panel
    private void OnErrorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;
        Dispatcher.UIThread.Post(FocusErrors);
    }

    private void FocusErrors()
    {
        if (DockFactory is not { } f)
            return;

        f.ShowTool(f.ErrorPanel);     // re-shows and activates; no-op if already visible
        f.OutputToolDock.IsExpanded = true;
    }

    // --------------------------------------------------------------------------------
    // construtor
    // --------------------------------------------------------------------------------

    public MainWindowVM(
        IDialogService dialogService,
        IErrorService errorService,
        OutputPanelVM outputPanel,
        ErrorPanelVM errorPanel,
        IFishboneDebugClientSessionFactory? debugSessionFactory = null)
    {
        this._dialogService = dialogService;
        this.ErrorService = errorService;
        this._outputPanel = outputPanel;
        _session = new ScriptSession(debugSessionFactory ?? new FishboneDebugClientSessionFactory(new FishboneDapHostLocator()));
        _session.Started += () => OnUi(() =>
        {
            ErrorService.ClearErrors();
            _outputPanel.Clear();
        });
        _session.Output += text => OnUi(() => _outputPanel.AppendBatch(text));
        _session.StateChanged += state => OnUi(() => DebugState = state);
        _session.Paused += (snapshot, session, isProgramExit) => OnUi(() => OnDebugPaused(snapshot, session, isProgramExit));
        _session.Continued += () => OnUi(() => WeakReferenceMessenger.Default.Send(new MessageDebugContinued()));

        Factory = new DockFactory(outputPanel, errorPanel);
        Layout = Factory?.CreateLayout();
        if (Layout != null)
            Factory?.InitLayout(Layout);

        if (DockFactory is { } dockFactory)
            dockFactory.ToolVisibilityChanged += OnToolVisibilityChanged;
        ErrorService.Errors.CollectionChanged += OnErrorsCollectionChanged;

        WeakReferenceMessenger.Default.Register<MessageExecute>(this);
        WeakReferenceMessenger.Default.Register<MessageVariableDetailsRequested>(this);

        LoadFunctionsMenu();
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    public async void Receive(MessageExecute m)
    {
        // whenever we receive the requested script code, execute it
        try
        {
            if (m.Mode == ScriptLaunchMode.Debug)
            {
                MessageExecute executionMessage = await PrepareDebugExecutionAsync(m);
                if (executionMessage.Script.Path is not null)
                    await DebugScriptAsync(executionMessage);
                return;
            }

            ScriptRunOutcome? outcome = await _session.RunAsync(m.Script.Code, m.Script.Directory, token =>
                OnUiAsync(() => _dialogService.ShowScriptInputAsync(token)));
            if (outcome is null)
                return;
            ReportErrors(outcome.Errors);
            if (outcome.Errors.Count == 0 && outcome.Environment is not null)
                WeakReferenceMessenger.Default.Send(new MessageExecutionFinished(m.Script.Name, outcome.Environment));
        }
        catch (Exception ex)
        {
            ReportErrors(ScriptExecutionError.From(ex));
        }
    }

    private void ReportErrors(IReadOnlyList<ScriptExecutionError> errors)
    {
        foreach (var error in errors)
            OnUi(() => ErrorService.AddError(error));
    }

    // session events can come from any thread. posting them keeps them in order. without an
    // app (in tests) they run inline: touching the dispatcher would start a loop that never ends
    private static void OnUi(Action action)
    {
        if (Avalonia.Application.Current is null)
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private static Task<T> OnUiAsync<T>(Func<Task<T>> action) =>
        Avalonia.Application.Current is null ? action() : Dispatcher.UIThread.InvokeAsync(action);

    public async void Receive(MessageVariableDetailsRequested m)
    {
        await _dialogService.ShowVariableDetailsAsync(m.Name, m.Value);
    }

    private static IDocumentDock? GetScriptsDock(IDockable? root)
    {
        if (root == null) return null;
        if (root.Id == "Scripts" && root is IDocumentDock docDock) return docDock;

        if (root is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var found = GetScriptsDock(child);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void LoadFunctionsMenu()
    {
        //var groupedFunctions = SharpFishbone.GetFunctionDescriptors()
        //    .GroupBy(descriptor => descriptor.Group);

        //foreach (var group in groupedFunctions)
        //{
        //    var groupMenuItem = new MenuItemViewModel { Header = group.Key };

        //    foreach (var descriptor in group)
        //    {
        //        var functionMenuItem = new MenuItemViewModel
        //        {
        //            Header = descriptor.Signature
        //        };

        //        groupMenuItem.Items.Add(functionMenuItem);
        //    }
        //    FunctionMenuItems.Add(groupMenuItem);
        //}
    }

    private async Task DebugScriptAsync(MessageExecute message)
    {
        _debugEditor = FindEditor(message.Script.SourceId);
        if (_debugEditor is not null)
            _debugEditor.BreakpointsChanged += OnDebugBreakpointsChanged;
        WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(message.Script.SourceId, true));
        try
        {
            ScriptRunOutcome? outcome = await _session.DebugAsync(message.Script.Path!, message.BreakpointLines, ApplyBreakpointResults);
            if (outcome is not null)
                ReportErrors(outcome.Errors);
        }
        finally
        {
            if (_debugEditor is not null)
                _debugEditor.BreakpointsChanged -= OnDebugBreakpointsChanged;
            _debugEditor = null;
            DebugState = FishboneDebugSessionState.Completed;
            WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(message.Script.SourceId, false));
            WeakReferenceMessenger.Default.Send(new MessageDebugLocationChanged(message.Script.SourceId, null));
        }
    }

    private void ApplyBreakpointResults(IReadOnlyList<FishboneBreakpointResult> results)
    {
        ScriptEditorVM? editor = _debugEditor;
        OnUi(() => editor?.ApplyBreakpointResults(results));
    }

    private ScriptEditorVM OpenRemoteSource(FishboneDebugSource source, string host, int port)
    {
        var scriptsDock = GetScriptsDock(Layout) ?? throw new InvalidOperationException("The scripts dock is unavailable.");
        string sourceId = source.Identity ?? $"fishbone-remote://{host}:{port}/{source.Reference}";
        ScriptEditorVM? existing = scriptsDock.VisibleDockables?.OfType<ScriptEditorVM>()
            .FirstOrDefault(editor => editor.IsRemote && editor.SourceId == sourceId);
        if (existing is not null)
        {
            existing.ScriptDocument = new AvaloniaEdit.Document.TextDocument(source.Content);
            scriptsDock.ActiveDockable = existing;
            return existing;
        }

        var editor = new ScriptEditorVM(source.Name, null, source.Content, sourceId, isRemote: true);
        scriptsDock.VisibleDockables ??= [];
        ScriptEditorVM? initialBlank = scriptsDock.VisibleDockables.OfType<ScriptEditorVM>().FirstOrDefault(candidate =>
            !candidate.IsRemote && candidate.ScriptPath is null && candidate.ScriptDocument.Text.Length == 0 &&
            candidate.BreakpointLines.Count == 0);
        if (initialBlank is not null)
            scriptsDock.VisibleDockables.Remove(initialBlank);
        scriptsDock.VisibleDockables.Add(editor);
        scriptsDock.ActiveDockable = editor;
        return editor;
    }

    private void OnDebugPaused(FishbonePauseSnapshot snapshot, IFishboneDebugClientSession session, bool isProgramExit)
    {
        FishboneDebugFrame? frame = snapshot.Frames.FirstOrDefault();

        // always surface the final variables. At the end-of-program pause, don't steal
        // focus or highlight a current line. The session stays paused there until the user
        // continues or stops, so collections in the variable panel can still be expanded:
        // their children are fetched from the debug host, which goes away once it finishes.
        if (_debugEditor is not null && !isProgramExit)
            ActivateEditor(_debugEditor.SourceId);
        WeakReferenceMessenger.Default.Send(new MessageDebugPaused(snapshot, session));
        WeakReferenceMessenger.Default.Send(new MessageDebugLocationChanged(
            _debugEditor?.SourceId ?? string.Empty, isProgramExit ? null : frame?.Line));
    }

    private async void OnDebugBreakpointsChanged(object? sender, EventArgs e)
    {
        ScriptEditorVM? editor = _debugEditor;
        if (editor is null)
            return;
        try
        {
            IReadOnlyList<FishboneBreakpointResult>? results = await _session.UpdateBreakpointsAsync(editor.BreakpointLines);
            if (results is not null)
                OnUi(() => editor.ApplyBreakpointResults(results));
        }
        catch (Exception exception)
        {
            ReportErrors(ScriptExecutionError.From(exception));
        }
    }

    private void ActivateEditor(string sourceId)
    {
        var scriptsDock = GetScriptsDock(Layout);
        var editor = scriptsDock?.VisibleDockables?.OfType<ScriptEditorVM>()
            .FirstOrDefault(candidate => candidate.SourceId == sourceId);
        if (scriptsDock is not null && editor is not null)
            scriptsDock.ActiveDockable = editor;
    }

    private ScriptEditorVM? FindEditor(string sourceId) => GetScriptsDock(Layout)?.VisibleDockables?
        .OfType<ScriptEditorVM>().FirstOrDefault(candidate => candidate.SourceId == sourceId);

    partial void OnDebugStateChanged(FishboneDebugSessionState value)
    {
        DebugCommand.NotifyCanExecuteChanged();
        ButtonRunCommand.NotifyCanExecuteChanged();
        ContinueCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StepIntoCommand.NotifyCanExecuteChanged();
        StepOverCommand.NotifyCanExecuteChanged();
        StepOutCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        AttachRemoteCommand.NotifyCanExecuteChanged();
    }

    // --------------------------------------------------------------------------------
    // commands
    // --------------------------------------------------------------------------------

    private bool CanStartExecution() => DebugState is FishboneDebugSessionState.Completed or FishboneDebugSessionState.Faulted;
    private bool CanPause() => DebugState == FishboneDebugSessionState.Running;
    private bool CanResume() => DebugState == FishboneDebugSessionState.Paused;
    private bool CanStop() => DebugState is FishboneDebugSessionState.Starting or FishboneDebugSessionState.Running or FishboneDebugSessionState.Paused;

    [RelayCommand(CanExecute = nameof(CanStartExecution))]
    private void OnButtonRun() => RunActiveScript(ScriptLaunchMode.Run);

    [RelayCommand(CanExecute = nameof(CanStartExecution))]
    private void Debug() => RunActiveScript(ScriptLaunchMode.Debug);

    private void RunActiveScript(ScriptLaunchMode mode)
    {
        if (GetScriptsDock(Layout)?.ActiveDockable is not ScriptEditorVM activeEditor || activeEditor.IsRemote)
            return;

        var scriptData = new Script(
            activeEditor.ScriptName, activeEditor.ScriptPath, activeEditor.ScriptDocument.Text, activeEditor.SourceId);
        WeakReferenceMessenger.Default.Send(new MessageExecute(scriptData, mode, activeEditor.BreakpointLines));
    }

    [RelayCommand(CanExecute = nameof(CanStartExecution))]
    private async Task AttachRemote()
    {
        RemoteAttachEndpoint? endpoint = await _dialogService.ShowRemoteAttachAsync();
        if (endpoint is not null)
            await AttachRemoteAsync(endpoint.Host, endpoint.Port);
    }

    public async Task AttachRemoteAsync(string host, int port)
    {
        IsVariableExplorerVisible = true;
        IsErrorsVisible = true;

        string? sourceId = null;
        try
        {
            ScriptRunOutcome? outcome = await _session.AttachAsync(host, port, source =>
                OnUiAsync(() =>
                {
                    _debugEditor = OpenRemoteSource(source, host, port);
                    sourceId = _debugEditor.SourceId;
                    _debugEditor.BreakpointsChanged += OnDebugBreakpointsChanged;
                    WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(sourceId, true));
                    return Task.FromResult(_debugEditor.BreakpointLines);
                }), ApplyBreakpointResults);
            if (outcome is not null)
                ReportErrors(outcome.Errors);
        }
        finally
        {
            if (_debugEditor is not null)
                _debugEditor.BreakpointsChanged -= OnDebugBreakpointsChanged;
            if (sourceId is not null)
            {
                WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(sourceId, false));
                WeakReferenceMessenger.Default.Send(new MessageDebugLocationChanged(sourceId, null));
            }
            _debugEditor = null;
            DebugState = FishboneDebugSessionState.Completed;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task Continue() => _session.ContinueAsync();

    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task Pause() => _session.PauseAsync();

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task StepInto() => _session.StepIntoAsync();

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task StepOver() => _session.StepOverAsync();

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task StepOut() => _session.StepOutAsync();

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task Stop() => _session.StopAsync();

    [RelayCommand]
    private async Task OnNewFile()
    {
        var scriptEditor = new ScriptEditorVM($"New{_newFileCounter++}", null, "");
        OpenEditorDocument(scriptEditor);
    }

    [RelayCommand]
    private async Task OnOpenFile()
    {
        var files = await _dialogService.OpenFileAsync();
        if (files?.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            var fileName = files[0].Name;

            var scriptEditor = new ScriptEditorVM(fileName, path, await File.ReadAllTextAsync(path));
            OpenEditorDocument(scriptEditor);
        }
    }

    public async Task OpenFileFromPathAsync(string path)
    {
        var scriptsDock = GetScriptsDock(Layout);
        var existing = scriptsDock?.VisibleDockables?.OfType<ScriptEditorVM>()
            .FirstOrDefault(editor => !editor.IsRemote && editor.ScriptPath == path);
        if (existing is not null)
        {
            scriptsDock!.ActiveDockable = existing;
            return;
        }

        var scriptEditor = new ScriptEditorVM(Path.GetFileName(path), path, await File.ReadAllTextAsync(path));
        OpenEditorDocument(scriptEditor);

        ScriptEditorVM? initialBlank = scriptsDock?.VisibleDockables?.OfType<ScriptEditorVM>().FirstOrDefault(candidate =>
            !ReferenceEquals(candidate, scriptEditor) && !candidate.IsRemote && candidate.ScriptPath is null &&
            !candidate.IsDirty && candidate.ScriptDocument.Text.Length == 0 && candidate.BreakpointLines.Count == 0);
        if (initialBlank is not null)
            scriptsDock!.VisibleDockables!.Remove(initialBlank);
    }

    /// <summary>The discovered sample scripts, bound by the Help > Samples menu.</summary>
    public IReadOnlyList<SampleDefinition> Samples => SampleCatalog.Samples;

    [RelayCommand]
    private void OpenSample(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        string code = SampleCatalog.Load(fileName);
        OpenEditorDocument(new ScriptEditorVM(fileName, null, code));
    }

    private void OpenEditorDocument(ScriptEditorVM scriptEditor)
    {
        var documentDock = GetScriptsDock(Layout);
        if (documentDock is null)
            return;

        documentDock.VisibleDockables ??= [];
        documentDock.VisibleDockables.Add(scriptEditor);
        documentDock.ActiveDockable = scriptEditor;
    }

    private async Task<MessageExecute> PrepareDebugExecutionAsync(MessageExecute message)
    {
        ScriptEditorVM? editor = FindEditor(message.Script.SourceId);
        if (editor is null)
            return message;

        if (editor.ScriptPath is null)
        {
            var file = await _dialogService.SaveFileAsync(editor.Title ?? "new_script.fb");
            if (file is null)
                return message with { Script = new Script(message.Script.Name, null, message.Script.Code, message.Script.SourceId) };
            editor.ScriptPath = file.Path.LocalPath;
            editor.Title = file.Name;
            editor.Id = editor.ScriptPath;
        }

        await File.WriteAllTextAsync(editor.ScriptPath, editor.ScriptDocument.Text);
        var savedScript = new Script(editor.ScriptName, editor.ScriptPath, editor.ScriptDocument.Text, editor.SourceId);
        return new MessageExecute(savedScript, ScriptLaunchMode.Debug, editor.BreakpointLines);
    }

    [RelayCommand]
    private async Task OnSaveFile()
    {
        var scriptsDock = GetScriptsDock(Layout);
        if (scriptsDock?.ActiveDockable is not ScriptEditorVM activeEditor)
            return;

        if (activeEditor.ScriptPath is null)
        {
            await OnSaveFileAs();
            return;
        }

        await File.WriteAllTextAsync(activeEditor.ScriptPath, activeEditor.ScriptDocument.Text);
        activeEditor.IsDirty = false;
    }

    [RelayCommand]
    private async Task OnSaveFileAs()
    {
        var scriptsDock = GetScriptsDock(Layout);
        if (scriptsDock?.ActiveDockable is not ScriptEditorVM activeEditor)
            return;

        var file = await _dialogService.SaveFileAsync(activeEditor.Title ?? "new_script.fb");

        if (file != null)
        {
            try
            {
                var path = file.Path.LocalPath;
                await File.WriteAllTextAsync(path, activeEditor.ScriptDocument.Text);

                activeEditor.Title = file.Name;
                activeEditor.ScriptPath = path;
                activeEditor.Id = path;
            }
            catch (Exception)
            {
                // ...?
            }
        }
    }

    [RelayCommand]
    private async Task CloseActiveTab()
    {
        var scriptsDock = GetScriptsDock(Layout);
        if (scriptsDock?.ActiveDockable is not ScriptEditorVM activeEditor)
            return;

        if (activeEditor.IsDirty)
        {
            var choice = await _dialogService.ShowSaveConfirmationAsync(activeEditor.Title ?? "Untitled");
            if (choice == SaveConfirmationResult.Cancel)
                return;

            if (choice == SaveConfirmationResult.Save)
            {
                await OnSaveFile();
                if (activeEditor.IsDirty)
                    return; // save was cancelled (e.g. Save As dialog dismissed)
            }
        }

        scriptsDock.VisibleDockables?.Remove(activeEditor);
    }

    [RelayCommand]
    private void OnCopy() => WeakReferenceMessenger.Default.Send(new MessageEditorAction(EditorAction.Copy));

    [RelayCommand]
    private void OnCut() => WeakReferenceMessenger.Default.Send(new MessageEditorAction(EditorAction.Cut));

    [RelayCommand]
    private void OnPaste() => WeakReferenceMessenger.Default.Send(new MessageEditorAction(EditorAction.Paste));

    [RelayCommand]
    private void OnUndo() => WeakReferenceMessenger.Default.Send(new MessageEditorAction(EditorAction.Undo));

    [RelayCommand]
    private void OnRedo() => WeakReferenceMessenger.Default.Send(new MessageEditorAction(EditorAction.Redo));

    [RelayCommand]
    private void OnAddLineComment() => WeakReferenceMessenger.Default.Send(new MessageEditorAction(EditorAction.AddLineComment));

    [RelayCommand]
    private void OnRemoveLineComment() => WeakReferenceMessenger.Default.Send(new MessageEditorAction(EditorAction.RemoveLineComment));

    [RelayCommand]
    private void InsertSnippet(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return;

        foreach (var snippet in FishboneSnippets.Core)
        {
            if (snippet.Header != header)
                continue;

            WeakReferenceMessenger.Default.Send(new MessageInsertSnippet(snippet.Template));
            return;
        }
    }
}


public class MenuItemViewModel
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = new();
}