using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Controls;
using Dock.Model.Core;
using Fishbone.Engine;
using SpineIDE.Models.Layout;
using SpineIDE.Models.Messages;
using SpineIDE.Services;
using SpineIDE.Views.Editor;

namespace SpineIDE.Views.Main;

public partial class MainWindowVM : ObservableObject, IRecipient<MessageExecute>
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    IDialogService _dialogService;
    public IErrorService ErrorService { get; set; }

    private static int _newFileCounter = 1;
    public ObservableCollection<MenuItemViewModel> FunctionMenuItems { get; } = new();
    [ObservableProperty] IFactory? _factory;
    [ObservableProperty] IRootDock? _layout;

    // --------------------------------------------------------------------------------
    // construtor
    // --------------------------------------------------------------------------------

    public MainWindowVM(IDialogService dialogService, IErrorService errorService)
    {
        this._dialogService = dialogService;
        this.ErrorService = errorService;

        Factory = new DockFactory();
        Layout = Factory?.CreateLayout();
        if (Layout != null)
            Factory?.InitLayout(Layout);

        WeakReferenceMessenger.Default.Register(this);

        LoadFunctionsMenu();
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    public async void Receive(MessageExecute m)
    {
        // whenever we receive the requested script code, execute it

        this.ErrorService.ClearErrors();

        string currentDirectory = Directory.GetCurrentDirectory();

        try
        {
            if (m.Script.Directory is not null && Directory.Exists(m.Script.Directory))
                Directory.SetCurrentDirectory(m.Script.Directory);

            var configuration = new FishboneConfiguration();
            var environment = FishboneEngine.Run(m.Script.Code, configuration);
            WeakReferenceMessenger.Default.Send(new MessageExecutionFinished(m.Script.Name, environment));
        }
        catch (Exception ex)
        {
            this.ErrorService.AddError(new ScriptExecutionError(ex.Message));
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }
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
        var documentDock = GetScriptsDock(Layout);

        if (documentDock != null)
        {
            documentDock.VisibleDockables ??= [];
            documentDock.VisibleDockables.Add(scriptEditor);
            documentDock.ActiveDockable = scriptEditor;
        }
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
            var documentDock = GetScriptsDock(Layout);

            if (documentDock != null)
            {
                documentDock.VisibleDockables ??= [];
                documentDock.VisibleDockables.Add(scriptEditor);
                documentDock.ActiveDockable = scriptEditor;
            }
        }
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
}

public class MenuItemViewModel
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<MenuItemViewModel> Items { get; } = new();
}