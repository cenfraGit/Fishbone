using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Controls;
using Dock.Model.Core;
using Fishbone.Core;
using Fishbone.DebugClient;
using Fishbone.Engine;
using Fishbone.Interpreter;
using Fishbone.Parser;
using SpineIDE.Models.Layout;
using SpineIDE.Models.Messages;
using SpineIDE.Models;
using SpineIDE.Panels;
using SpineIDE.Services;
using SpineIDE.Views.Editor;

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

    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly SemaphoreSlim _breakpointSyncGate = new(1, 1);
    private readonly IFishboneDebugClientSessionFactory _debugSessionFactory;
    private CancellationTokenSource? _scriptCTS;
    private IFishboneDebugClientSession? _debugSession;
    private ScriptEditorVM? _debugEditor;
    private int _breakpointRevision;
    private int _executionVersion;

    [ObservableProperty] private FishboneDebugSessionState _debugState = FishboneDebugSessionState.Completed;

    public ObservableCollection<MenuItemViewModel> FunctionMenuItems { get; } = new();
    [ObservableProperty] IFactory? _factory;
    [ObservableProperty] IRootDock? _layout;

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
        _debugSessionFactory = debugSessionFactory ?? new FishboneDebugClientSessionFactory(new FishboneDapHostLocator());

        Factory = new DockFactory(outputPanel, errorPanel);
        Layout = Factory?.CreateLayout();
        if (Layout != null)
            Factory?.InitLayout(Layout);

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

        int executionVersion = Interlocked.Increment(ref _executionVersion);
        _scriptCTS?.Cancel();
        await _executionGate.WaitAsync();

        try
        {
            if (executionVersion != Volatile.Read(ref _executionVersion))
                return;

            using var currentCTS = new CancellationTokenSource();
            _scriptCTS = currentCTS;
            CancellationToken localToken = currentCTS.Token;

            this.ErrorService.ClearErrors();
            _outputPanel.Clear();

            string currentDirectory = Directory.GetCurrentDirectory();

            try
            {
                MessageExecute executionMessage = m;
                if (m.Mode == ScriptLaunchMode.Debug)
                {
                    executionMessage = await PrepareDebugExecutionAsync(m);
                    if (executionMessage.Script.Path is null)
                        return;
                }

                if (executionMessage.Script.Directory is not null && Directory.Exists(executionMessage.Script.Directory))
                    Directory.SetCurrentDirectory(executionMessage.Script.Directory);

                var result = executionMessage.Mode == ScriptLaunchMode.Debug
                    ? await ExecuteDebugSessionAsync(executionMessage, executionVersion, localToken)
                    : await ExecuteScriptAsync(executionMessage, executionVersion, localToken);
                if (result.Error is not null)
                {
                    await ReportScriptErrorAsync(result.Error);
                    return;
                }

                if (executionMessage.Mode == ScriptLaunchMode.Run &&
                    executionVersion == Volatile.Read(ref _executionVersion) && !localToken.IsCancellationRequested)
                    WeakReferenceMessenger.Default.Send(new MessageExecutionFinished(executionMessage.Script.Name, result.Environment!));
            }
            catch (OperationCanceledException)
            {
                if (executionVersion == Volatile.Read(ref _executionVersion))
                    _outputPanel.AppendLine("[FishboneEngine] Execution cancelled.");
            }
            catch (Exception ex)
            {
                if (executionVersion == Volatile.Read(ref _executionVersion))
                    await ReportScriptErrorAsync(ex);
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDirectory);
                if (ReferenceEquals(_scriptCTS, currentCTS))
                    _scriptCTS = null;
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private async Task ReportScriptErrorAsync(Exception exception)
    {
        if (exception is FishboneParseException parseException)
        {
            foreach (var error in parseException.Errors)
                await AddErrorAsync(error.Message, error.Line > 0 ? error.Line : null, error.Column > 0 ? error.Column : null);
            return;
        }

        int? line = null;
        int? column = null;
        if (exception is FishboneRuntimeException runtimeException)
        {
            line = runtimeException.Line > 0 ? runtimeException.Line : null;
            column = runtimeException.Column > 0 ? runtimeException.Column : null;
        }
        await AddErrorAsync(exception.Message, line, column);

        async Task AddErrorAsync(string message, int? line, int? column)
        {
            if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
            {
                ErrorService.AddError(new ScriptExecutionError(message, line, column));
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ErrorService.AddError(new ScriptExecutionError(message, line, column));
            });
        }
    }

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

    private async Task<ScriptExecutionResult> ExecuteScriptAsync(
        MessageExecute m,
        int executionVersion,
        CancellationToken cancellationToken)
    {
        string scriptCode = m.Script.Code;
        var configuration = new FishboneConfiguration();
        var outputBuffer = new ScriptOutputBuffer();

        FishbonePluginLoader.LoadPlugins(FishbonePluginLoader.DefaultPluginsDirectory, configuration);

        configuration.AddBuiltIn("print", new Action<object?>(outputBuffer.Append));
        configuration.AddBuiltIn("println", new Action<object?>(outputBuffer.AppendLine));
        configuration.AddBuiltIn("input", new Func<string>(() =>
            ReadScriptInput(outputBuffer, executionVersion, cancellationToken)));

        Task<ScriptExecutionResult> executionTask = Task.Run(
            () =>
            {
                try
                {
                    return new ScriptExecutionResult(
                        FishboneEngine.Run(scriptCode, configuration, cancellationToken),
                        null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new ScriptExecutionResult(null, ex);
                }
            },
            cancellationToken);

        try
        {
            while (!executionTask.IsCompleted)
            {
                await Task.WhenAny(executionTask, Task.Delay(50));
                FlushOutput(outputBuffer, executionVersion, cancellationToken);
            }

            return await executionTask;
        }
        finally
        {
            FlushOutput(outputBuffer, executionVersion, cancellationToken);
        }
    }

    private async Task<ScriptExecutionResult> ExecuteDebugSessionAsync(
        MessageExecute message,
        int executionVersion,
        CancellationToken cancellationToken)
    {
        string scriptPath = message.Script.Path!;
        var completion = new TaskCompletionSource<ScriptExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IFishboneDebugClientSession session = _debugSessionFactory.CreateLaunched(scriptPath);
        _debugSession = session;
        _debugEditor = FindEditor(message.Script.SourceId);
        if (_debugEditor is not null)
            _debugEditor.BreakpointsChanged += OnDebugBreakpointsChanged;
        session.EventReceived += OnDebugEventReceived;
        WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(message.Script.SourceId, true));

        void Complete(ScriptExecutionResult result) => completion.TrySetResult(result);
        void HandleCompletion(object? sender, FishboneDebugEvent debugEvent)
        {
            switch (debugEvent)
            {
                case FishboneDebugTerminated terminated:
                    Complete(terminated.ExitCode is null or 0
                        ? new ScriptExecutionResult(null, null)
                        : new ScriptExecutionResult(null, new InvalidOperationException($"fishbone-dap exited with code {terminated.ExitCode}.")));
                    break;
                case FishboneDebugFailed failed:
                    Complete(new ScriptExecutionResult(null, failed.Exception));
                    break;
            }
        }

        session.EventReceived += HandleCompletion;
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await session.ConnectAsync(stopOnEntry: false, cancellationToken);
            IReadOnlyList<FishboneBreakpointResult> breakpointResults =
                await session.ConfigureAsync(message.BreakpointLines, cancellationToken);
            _debugEditor?.ApplyBreakpointResults(breakpointResults);
            return await completion.Task;
        }
        finally
        {
            session.EventReceived -= HandleCompletion;
            session.EventReceived -= OnDebugEventReceived;
            if (_debugEditor is not null)
                _debugEditor.BreakpointsChanged -= OnDebugBreakpointsChanged;
            await session.DisposeAsync();
            if (ReferenceEquals(_debugSession, session))
                _debugSession = null;
            _debugEditor = null;
            DebugState = FishboneDebugSessionState.Completed;
            WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(message.Script.SourceId, false));
            WeakReferenceMessenger.Default.Send(new MessageDebugLocationChanged(message.Script.SourceId, null));
        }
    }

    private async Task ExecuteRemoteAttachAsync(string host, int port, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        IFishboneDebugClientSession session = _debugSessionFactory.CreateAttached(host, port);
        _debugSession = session;
        session.EventReceived += OnDebugEventReceived;

        void HandleCompletion(object? sender, FishboneDebugEvent debugEvent)
        {
            if (debugEvent is FishboneDebugTerminated)
                completion.TrySetResult(null);
            else if (debugEvent is FishboneDebugFailed failed)
                completion.TrySetResult(failed.Exception);
        }

        session.EventReceived += HandleCompletion;
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        string? sourceId = null;
        try
        {
            FishboneDebugSource source = await session.ConnectAsync(stopOnEntry: true, cancellationToken);
            _debugEditor = OpenRemoteSource(source, host, port);
            sourceId = _debugEditor.SourceId;
            _debugEditor.BreakpointsChanged += OnDebugBreakpointsChanged;
            WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(sourceId, true));

            IReadOnlyList<FishboneBreakpointResult> results =
                await session.ConfigureAsync(_debugEditor.BreakpointLines, cancellationToken);
            _debugEditor.ApplyBreakpointResults(results);

            Exception? error = await completion.Task;
            if (error is not null)
                throw error;
        }
        finally
        {
            session.EventReceived -= HandleCompletion;
            session.EventReceived -= OnDebugEventReceived;
            if (_debugEditor is not null)
                _debugEditor.BreakpointsChanged -= OnDebugBreakpointsChanged;
            await session.DisposeAsync();
            if (ReferenceEquals(_debugSession, session))
                _debugSession = null;
            if (sourceId is not null)
            {
                WeakReferenceMessenger.Default.Send(new MessageDebugEditingChanged(sourceId, false));
                WeakReferenceMessenger.Default.Send(new MessageDebugLocationChanged(sourceId, null));
            }
            _debugEditor = null;
            DebugState = FishboneDebugSessionState.Completed;
        }
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

    private void OnDebugEventReceived(object? sender, FishboneDebugEvent debugEvent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (debugEvent)
            {
                case FishboneDebugStateChanged state:
                    DebugState = state.State;
                    break;
                case FishboneDebugOutput output:
                    _outputPanel.AppendBatch(output.Text);
                    break;
                case FishboneDebugPaused paused when sender is IFishboneDebugClientSession session:
                    FishboneDebugFrame? frame = paused.Snapshot.Frames.FirstOrDefault();
                    if (_debugEditor is not null)
                        ActivateEditor(_debugEditor.SourceId);
                    WeakReferenceMessenger.Default.Send(new MessageDebugPaused(paused.Snapshot, session));
                    WeakReferenceMessenger.Default.Send(new MessageDebugLocationChanged(
                        _debugEditor?.SourceId ?? string.Empty, frame?.Line));
                    break;
                case FishboneDebugContinued:
                    WeakReferenceMessenger.Default.Send(new MessageDebugContinued());
                    break;
            }
        });
    }

    private async void OnDebugBreakpointsChanged(object? sender, EventArgs e)
    {
        IFishboneDebugClientSession? session = _debugSession;
        ScriptEditorVM? editor = _debugEditor;
        int requestedRevision = Interlocked.Increment(ref _breakpointRevision);
        if (session is null || editor is null || session.State is FishboneDebugSessionState.Completed or FishboneDebugSessionState.Faulted)
            return;
        await _breakpointSyncGate.WaitAsync();
        try
        {
            if (requestedRevision != Volatile.Read(ref _breakpointRevision))
                return;
            IReadOnlyList<FishboneBreakpointResult> results = await session.SetBreakpointsAsync(editor.BreakpointLines);
            if (requestedRevision == Volatile.Read(ref _breakpointRevision))
                await Dispatcher.UIThread.InvokeAsync(() => editor.ApplyBreakpointResults(results));
        }
        catch (Exception exception)
        {
            await ReportScriptErrorAsync(exception);
        }
        finally
        {
            _breakpointSyncGate.Release();
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

    private void FlushOutput(
        ScriptOutputBuffer outputBuffer,
        int executionVersion,
        CancellationToken cancellationToken)
    {
        string output = outputBuffer.DrainPending();
        if (output.Length == 0)
            return;

        if (executionVersion == Volatile.Read(ref _executionVersion) && !cancellationToken.IsCancellationRequested)
            _outputPanel.AppendBatch(output);
    }

    private string ReadScriptInput(
        ScriptOutputBuffer outputBuffer,
        int executionVersion,
        CancellationToken cancellationToken)
    {
        using var request = new ScriptInputRequest(cancellationToken);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (executionVersion != Volatile.Read(ref _executionVersion) || cancellationToken.IsCancellationRequested)
                {
                    request.Cancel();
                    return;
                }

                FlushOutput(outputBuffer, executionVersion, cancellationToken);
                string value = await _dialogService.ShowScriptInputAsync(cancellationToken);

                if (executionVersion != Volatile.Read(ref _executionVersion) || cancellationToken.IsCancellationRequested)
                {
                    request.Cancel();
                    return;
                }

                outputBuffer.AppendLine(value);
                FlushOutput(outputBuffer, executionVersion, cancellationToken);
                request.Submit(value);
            }
            catch (OperationCanceledException)
            {
                request.Cancel();
            }
            catch (Exception ex)
            {
                request.Fail(ex);
            }
        });

        return request.Wait();
    }

    // --------------------------------------------------------------------------------
    // commands
    // --------------------------------------------------------------------------------

    private bool CanStartExecution() => DebugState is FishboneDebugSessionState.Completed or FishboneDebugSessionState.Faulted;
    private bool CanPause() => DebugState == FishboneDebugSessionState.Running;
    private bool CanResume() => DebugState == FishboneDebugSessionState.Paused;
    private bool CanStop() => DebugState is FishboneDebugSessionState.Starting or FishboneDebugSessionState.Running or FishboneDebugSessionState.Paused;

    [RelayCommand(CanExecute = nameof(CanStartExecution))]
    private async Task OnButtonRun()
    {
        WeakReferenceMessenger.Default.Send(new MessageRunActiveScript(ScriptLaunchMode.Run));
    }

    [RelayCommand(CanExecute = nameof(CanStartExecution))]
    private void Debug() => WeakReferenceMessenger.Default.Send(new MessageRunActiveScript(ScriptLaunchMode.Debug));

    [RelayCommand(CanExecute = nameof(CanStartExecution))]
    private async Task AttachRemote()
    {
        RemoteAttachEndpoint? endpoint = await _dialogService.ShowRemoteAttachAsync();
        if (endpoint is not null)
            await AttachRemoteAsync(endpoint.Host, endpoint.Port);
    }

    public async Task AttachRemoteAsync(string host, int port)
    {
        Interlocked.Increment(ref _executionVersion);
        _scriptCTS?.Cancel();
        await _executionGate.WaitAsync();
        try
        {
            using var currentCTS = new CancellationTokenSource();
            _scriptCTS = currentCTS;
            ErrorService.ClearErrors();
            _outputPanel.Clear();
            try
            {
                await ExecuteRemoteAttachAsync(host, port, currentCTS.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                await ReportScriptErrorAsync(exception);
            }
            finally
            {
                if (ReferenceEquals(_scriptCTS, currentCTS))
                    _scriptCTS = null;
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task Continue() { if (_debugSession is not null) await _debugSession.ContinueAsync(); }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task Pause() { if (_debugSession is not null) await _debugSession.PauseAsync(); }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task StepInto() { if (_debugSession is not null) await _debugSession.StepIntoAsync(); }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task StepOver() { if (_debugSession is not null) await _debugSession.StepOverAsync(); }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task StepOut() { if (_debugSession is not null) await _debugSession.StepOutAsync(); }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task Stop()
    {
        if (_debugSession is not null)
            await _debugSession.StopAsync();
        if (_debugSession?.Ownership != FishboneDebugSessionOwnership.Attached)
            _scriptCTS?.Cancel();
    }

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

internal record ScriptExecutionResult(FishboneEnvironment? Environment, Exception? Error);

public class MenuItemViewModel
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = new();
}