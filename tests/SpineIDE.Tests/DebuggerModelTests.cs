using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Fishbone.DebugClient;
using SpineIDE.Models.Messages;
using SpineIDE.Panels;
using SpineIDE.Services;
using SpineIDE.Views.Editor;
using SpineIDE.Views.Main;

namespace SpineIDE.Tests;

public class DebuggerModelTests
{
    [Fact]
    public void BreakpointAnchorFollowsInsertedLines()
    {
        var editor = new ScriptEditorVM("test.fb", null, "first\nsecond\nthird");
        editor.ToggleBreakpoint(2);

        editor.ScriptDocument.Insert(0, "new\n");

        Assert.Equal([3], editor.BreakpointLines);
        editor.ToggleBreakpoint(3);
        Assert.Empty(editor.BreakpointLines);
    }

    [Fact]
    public void ToggleBreakpointIgnoresLineOutsideCurrentDocument()
    {
        var editor = new ScriptEditorVM("test.fb", null, "only line");

        editor.ToggleBreakpoint(2);

        Assert.Empty(editor.BreakpointLines);
    }

    [Fact]
    public void VariableExplorerDisplaysPausedSnapshot()
    {
        var explorer = new VariableExplorerVM();
        var session = new StubDebugSession();
        var variable = new FishboneDebugVariable("answer", "42", "Int32", null, null, null);
        var scope = new FishboneDebugScope("Visible Variables", new FishboneVariableHandle(1, 1), [variable]);
        var frame = new FishboneDebugFrame(1, "<script>", "test.fb", 2, 1, [scope]);
        var snapshot = new FishbonePauseSnapshot(1, "breakpoint", null, [frame], null);

        explorer.Receive(new MessageDebugPaused(snapshot, session));

        var scopeItem = Assert.Single(explorer.Variables);
        var item = Assert.Single(scopeItem.Children);
        Assert.Equal("answer", item.Name);
        Assert.Equal("42", item.ValueDisplay);
    }

    [Fact]
    public void DebugCommandsReflectSessionState()
    {
        var errors = new ErrorService();
        var viewModel = new MainWindowVM(
            new StubDialogService(),
            errors,
            new OutputPanelVM(),
            new ErrorPanelVM(errors));

        Assert.True(viewModel.DebugCommand.CanExecute(null));
        Assert.False(viewModel.ContinueCommand.CanExecute(null));

        viewModel.DebugState = FishboneDebugSessionState.Paused;

        Assert.False(viewModel.DebugCommand.CanExecute(null));
        Assert.True(viewModel.ContinueCommand.CanExecute(null));
        Assert.True(viewModel.StepOverCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
    }

    [Fact]
    public void BreakpointVerificationIsAppliedWithoutMovingAnchor()
    {
        var editor = new ScriptEditorVM("test.fb", null, "first\nsecond");
        editor.ToggleBreakpoint(2);

        editor.ApplyBreakpointResults([new FishboneBreakpointResult(2, false, "invalid")]);

        Assert.False(editor.IsBreakpointVerified(2));
        Assert.Equal([2], editor.BreakpointLines);
    }

    [Fact]
    public async Task VariableChildrenLoadLazilyAndUnloadedHandlesExpireOnContinue()
    {
        var explorer = new VariableExplorerVM();
        var session = new StubDebugSession
        {
            VariablesToReturn = [new FishboneDebugVariable("[0]", "7", "Int32", null, null, null)]
        };
        var list = new FishboneDebugVariable("items", "[1 item]", "List", new FishboneVariableHandle(1, 9), null, 1);
        var scope = new FishboneDebugScope("Visible Variables", new FishboneVariableHandle(1, 1), [list]);
        var snapshot = new FishbonePauseSnapshot(1, "breakpoint", null,
            [new FishboneDebugFrame(1, "<script>", "test.fb", 2, 1, [scope])], null);
        explorer.Receive(new MessageDebugPaused(snapshot, session));
        VariableItem item = explorer.Variables[0].Children[0];

        await item.ToggleExpandedCommand.ExecuteAsync(null);

        Assert.Equal(1, session.VariableRequests);
        Assert.Equal("[0]", Assert.Single(item.Children).Name);

        var unloaded = new VariableItem
        {
            Name = "other",
            ChildrenHandle = new FishboneVariableHandle(1, 10),
            Session = session
        };
        explorer.Variables.Add(unloaded);
        explorer.Receive(new MessageDebugContinued());
        Assert.False(unloaded.CanLoadChildren);
        Assert.Single(item.Children);
    }

    private sealed class StubDebugSession : IFishboneDebugClientSession
    {
        public event EventHandler<FishboneDebugEvent>? EventReceived;
        public FishboneDebugSessionState State => FishboneDebugSessionState.Paused;
        public string ScriptPath => "test.fb";
        public IReadOnlyList<FishboneDebugVariable> VariablesToReturn { get; init; } = [];
        public int VariableRequests { get; private set; }
        public Task StartAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FishboneBreakpointResult>> SetBreakpointsAsync(IReadOnlyList<int> lines, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FishboneBreakpointResult>>([]);
        public Task<IReadOnlyList<FishboneDebugVariable>> GetVariablesAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default)
        {
            VariableRequests++;
            return Task.FromResult(VariablesToReturn);
        }
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepIntoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubDialogService : IDialogService
    {
        public void Initialize(Window window) { }
        public Task<IReadOnlyList<IStorageFile>?> OpenFileAsync() => Task.FromResult<IReadOnlyList<IStorageFile>?>(null);
        public Task<IStorageFile?> SaveFileAsync(string suggestedName = "script.fb") => Task.FromResult<IStorageFile?>(null);
        public Task ShowVariableDetailsAsync(string name, object? value) => Task.CompletedTask;
        public Task<string> ShowScriptInputAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);
    }
}