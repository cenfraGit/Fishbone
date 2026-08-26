using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Dock.Model.Controls;
using Dock.Model.Core;
using Fishbone.Engine;
using SpineIDE.Models;
using SpineIDE.Panels;
using SpineIDE.Services;
using SpineIDE.Views.Editor;
using SpineIDE.Views.Main;

namespace SpineIDE.Tests;

public class SampleCatalogTests
{
    [Fact]
    public void Samples_AreDiscoveredFromEmbeddedResources()
    {
        Assert.NotEmpty(SampleCatalog.Samples);
        // display name derived from the file name
        Assert.Contains(SampleCatalog.Samples,
            sample => sample.FileName == "bubble_sort.fb" && sample.DisplayName == "Bubble Sort");
        // display name from the sample's "// title:" header
        Assert.Contains(SampleCatalog.Samples,
            sample => sample.FileName == "edge_detect.fb" && sample.DisplayName == "OpenCV Edge Detection");
    }

    [Fact]
    public void EveryDiscoveredSample_LoadsAndParses()
    {
        foreach (SampleDefinition sample in SampleCatalog.Samples)
        {
            Assert.False(string.IsNullOrWhiteSpace(sample.DisplayName));
            string code = SampleCatalog.Load(sample.FileName);
            Assert.False(string.IsNullOrWhiteSpace(code));
            FishboneProgram.FromSourceCode(code); // throws if a sample has syntax-rotted
        }
    }

    [Fact]
    public void Load_UnknownSample_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => SampleCatalog.Load("missing.fb"));
    }

    [Fact]
    public void BubbleSort_ExecutesAndSortsTheList()
    {
        string code = SampleCatalog.Load("bubble_sort.fb");
        var output = new StringBuilder();
        var configuration = CreateOutputConfiguration(output);

        FishboneProgram.Run(code, configuration);

        Assert.Contains("3, 12, 21, 54, 89", output.ToString());
    }

    [Fact]
    public void AreaCircle_ExecutesWithConfiguredInput()
    {
        string code = SampleCatalog.Load("area_circle.fb");
        var output = new StringBuilder();
        var configuration = CreateOutputConfiguration(output);
        configuration.AddBuiltIn("input", new Func<string>(() => "2"));

        FishboneProgram.Run(code, configuration);

        Assert.Contains("12.566", output.ToString());
    }

    [Fact]
    public void OpenSampleCommand_OpensEditableUnsavedDocument()
    {
        var errorService = new ErrorService();
        var viewModel = new MainWindowVM(
            new StubDialogService(),
            errorService,
            new OutputPanelVM(),
            new ErrorPanelVM(errorService));

        viewModel.OpenSampleCommand.Execute("area_circle.fb");

        var scriptsDock = FindScriptsDock(viewModel.Layout);
        var editor = Assert.IsType<ScriptEditorVM>(scriptsDock?.ActiveDockable);
        Assert.Null(editor.ScriptPath);
        Assert.Equal("area_circle.fb", editor.Title);
        Assert.Equal(SampleCatalog.Load("area_circle.fb"), editor.ScriptDocument.Text);
    }

    private static FishboneConfiguration CreateOutputConfiguration(StringBuilder output)
    {
        var configuration = new FishboneConfiguration();
        configuration.AddBuiltIn("print", new Action<object?>(value => output.Append(value)));
        configuration.AddBuiltIn("println", new Action<object?>(value => output.AppendLine(value?.ToString())));
        return configuration;
    }

    private static IDocumentDock? FindScriptsDock(IDockable? dockable)
    {
        if (dockable is IDocumentDock documentDock && dockable.Id == "Scripts")
            return documentDock;

        if (dockable is IDock dock && dock.VisibleDockables is not null)
            foreach (IDockable child in dock.VisibleDockables)
            {
                IDocumentDock? result = FindScriptsDock(child);
                if (result is not null)
                    return result;
            }

        return null;
    }

    private sealed class StubDialogService : IDialogService
    {
        public void Initialize(Window window) { }

        public Task<IReadOnlyList<IStorageFile>?> OpenFileAsync() =>
            Task.FromResult<IReadOnlyList<IStorageFile>?>(null);

        public Task<IStorageFile?> SaveFileAsync(string suggestedName = "script.fb") =>
            Task.FromResult<IStorageFile?>(null);

        public Task ShowVariableDetailsAsync(string name, object? value) => Task.CompletedTask;

        public Task<string> ShowScriptInputAsync(CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);

        public Task<RemoteAttachEndpoint?> ShowRemoteAttachAsync() =>
            Task.FromResult<RemoteAttachEndpoint?>(null);
        public Task<SaveConfirmationResult> ShowSaveConfirmationAsync(string fileName) => Task.FromResult(SaveConfirmationResult.Discard);
    }
}