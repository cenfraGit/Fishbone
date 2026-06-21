using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Controls;
using Dock.Model.Core;
using Fishbone.Core;
using Fishbone.Engine;
using SpineIDE.Models.Layout;
using SpineIDE.Models.Messages;
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
    private CancellationTokenSource? _scriptCTS;
    private int _executionVersion;

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
        ErrorPanelVM errorPanel)
    {
        this._dialogService = dialogService;
        this.ErrorService = errorService;
        this._outputPanel = outputPanel;

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
                if (m.Script.Directory is not null && Directory.Exists(m.Script.Directory))
                    Directory.SetCurrentDirectory(m.Script.Directory);

                var result = await ExecuteScriptAsync(m, executionVersion, localToken);
                if (result.Error is not null)
                {
                    await ReportScriptErrorAsync(result.Error);
                    return;
                }

                if (executionVersion == Volatile.Read(ref _executionVersion) && !localToken.IsCancellationRequested)
                    WeakReferenceMessenger.Default.Send(new MessageExecutionFinished(m.Script.Name, result.Environment!));
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
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ErrorService.AddError(new ScriptExecutionError(exception.Message));
        });
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

        configuration.RegisterBuiltIn("print", new Action<object?>(outputBuffer.Append));
        configuration.RegisterBuiltIn("println", new Action<object?>(outputBuffer.AppendLine));
        configuration.RegisterBuiltIn("input", new Func<string>(() =>
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

    [RelayCommand]
    private async Task OnButtonRun()
    {
        // send request to run active script
        WeakReferenceMessenger.Default.Send(new MessageRunActiveScript());
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