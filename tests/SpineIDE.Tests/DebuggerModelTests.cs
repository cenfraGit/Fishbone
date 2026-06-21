using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Fishbone.Debugging;
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
        var snapshot = new DebugPauseSnapshot(
            new DebugSourceLocation("test.fb", 2, 1),
            DebugPauseReason.Breakpoint,
            [new DebugVariableSnapshot("answer", 42)],
            ImmutableArray<DebugCallFrameSnapshot>.Empty,
            null);

        explorer.Receive(new MessageDebugPaused(snapshot));

        var variable = Assert.Single(explorer.Variables);
        Assert.Equal("answer", variable.Name);
        Assert.Equal("42", variable.ValueDisplay);
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

        viewModel.DebugState = DebugSessionState.Paused;

        Assert.False(viewModel.DebugCommand.CanExecute(null));
        Assert.True(viewModel.ContinueCommand.CanExecute(null));
        Assert.True(viewModel.StepOverCommand.CanExecute(null));
        Assert.True(viewModel.StopCommand.CanExecute(null));
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