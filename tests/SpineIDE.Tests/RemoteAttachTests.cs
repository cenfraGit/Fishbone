using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Dock.Model.Controls;
using Dock.Model.Core;
using Fishbone.DebugClient;
using SpineIDE.Panels;
using SpineIDE.Services;
using SpineIDE.Views.Editor;
using SpineIDE.Views.Main;

namespace SpineIDE.Tests;

public class RemoteAttachTests
{
    [Fact]
    public void CommandLineParsesLocalAttachPort()
    {
        using var errors = new StringWriter();

        bool parsed = SpineIdeStartupOptions.TryParse(["--attach", "5123"], errors, out var options);

        Assert.True(parsed);
        Assert.Equal(5123, options.AttachPort);
        Assert.Equal(string.Empty, errors.ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void CommandLineRejectsInvalidAttachPort(string value)
    {
        using var errors = new StringWriter();

        bool parsed = SpineIdeStartupOptions.TryParse(["--attach", value], errors, out _);

        Assert.False(parsed);
        Assert.NotEmpty(errors.ToString());
    }

    [Fact]
    public async Task RemoteAttachCreatesAndRetainsReadOnlySourceSnapshot()
    {
        var errors = new ErrorService();
        var session = new StubAttachedSession();
        var viewModel = new MainWindowVM(
            new StubDialogService(),
            errors,
            new OutputPanelVM(),
            new ErrorPanelVM(errors),
            new StubSessionFactory(session));

        await viewModel.AttachRemoteAsync("127.0.0.1", 4711);

        IDocumentDock? scripts = FindScriptsDock(viewModel.Layout);
        var editor = Assert.IsType<ScriptEditorVM>(scripts?.ActiveDockable);
        Assert.True(editor.IsRemote);
        Assert.True(editor.IsReadOnly);
        Assert.False(editor.IsDebugging);
        Assert.False(editor.CanToggleBreakpoints);
        Assert.Equal("let remoteValue = 7;", editor.ScriptDocument.Text);
        Assert.Equal("[Remote] hosted.fb", editor.Title);
        Assert.True(session.StopOnEntryRequested);
        Assert.True(session.Configured);
        Assert.Empty(errors.Errors);
    }

    [Fact]
    public void RemoteBreakpointsAreEnabledOnlyDuringActiveSession()
    {
        var editor = new ScriptEditorVM(
            "hosted.fb", null, "first\nsecond", "fishbone://host/hosted.fb", isRemote: true);

        editor.ToggleBreakpoint(2);
        Assert.Empty(editor.BreakpointLines);

        editor.IsDebugging = true;
        editor.ToggleBreakpoint(2);
        Assert.Equal([2], editor.BreakpointLines);

        editor.IsDebugging = false;
        editor.ToggleBreakpoint(2);
        Assert.Equal([2], editor.BreakpointLines);
    }

    [Fact]
    public async Task RemoteConnectionFailureIsReportedWithoutClosingIde()
    {
        var errors = new ErrorService();
        var session = new StubAttachedSession { ConnectError = new IOException("Connection refused") };
        var viewModel = new MainWindowVM(
            new StubDialogService(), errors, new OutputPanelVM(), new ErrorPanelVM(errors),
            new StubSessionFactory(session));

        await viewModel.AttachRemoteAsync("127.0.0.1", 4711);

        Assert.Equal("Connection refused", Assert.Single(errors.Errors).ExMessage);
        Assert.NotNull(viewModel.Layout);
        Assert.True(viewModel.AttachRemoteCommand.CanExecute(null));
    }

    private static IDocumentDock? FindScriptsDock(IDockable? dockable)
    {
        if (dockable is IDocumentDock documentDock && dockable.Id == "Scripts")
            return documentDock;
        if (dockable is IDock dock && dock.VisibleDockables is not null)
            foreach (IDockable child in dock.VisibleDockables)
                if (FindScriptsDock(child) is { } result)
                    return result;
        return null;
    }

    private sealed class StubAttachedSession : IFishboneDebugClientSession
    {
        public event EventHandler<FishboneDebugEvent>? EventReceived;
        public FishboneDebugSessionState State { get; private set; } = FishboneDebugSessionState.Starting;
        public FishboneDebugSessionOwnership Ownership => FishboneDebugSessionOwnership.Attached;
        public FishboneDebugSource? Source { get; private set; }
        public bool StopOnEntryRequested { get; private set; }
        public bool Configured { get; private set; }
        public Exception? ConnectError { get; init; }

        public Task<FishboneDebugSource> ConnectAsync(bool stopOnEntry = false, CancellationToken cancellationToken = default)
        {
            if (ConnectError is not null)
                return Task.FromException<FishboneDebugSource>(ConnectError);
            StopOnEntryRequested = stopOnEntry;
            Source = new FishboneDebugSource("hosted.fb", "fishbone://host/hosted.fb", 1, "let remoteValue = 7;", "text/plain");
            return Task.FromResult(Source);
        }

        public Task<IReadOnlyList<FishboneBreakpointResult>> ConfigureAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default)
        {
            Configured = true;
            State = FishboneDebugSessionState.Completed;
            EventReceived?.Invoke(this, new FishboneDebugTerminated(0));
            return Task.FromResult<IReadOnlyList<FishboneBreakpointResult>>([]);
        }

        public Task StartAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FishboneBreakpointResult>> SetBreakpointsAsync(IReadOnlyList<int> lines, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FishboneBreakpointResult>>([]);
        public Task<IReadOnlyList<FishboneDebugVariable>> GetVariablesAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FishboneDebugVariable>>([]);
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepIntoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubSessionFactory(IFishboneDebugClientSession session) : IFishboneDebugClientSessionFactory
    {
        public IFishboneDebugClientSession CreateLaunched(string scriptPath) => throw new NotSupportedException();
        public IFishboneDebugClientSession CreateAttached(string host, int port) => session;
    }

    private sealed class StubDialogService : IDialogService
    {
        public void Initialize(Window window) { }
        public Task<IReadOnlyList<IStorageFile>?> OpenFileAsync() => Task.FromResult<IReadOnlyList<IStorageFile>?>(null);
        public Task<IStorageFile?> SaveFileAsync(string suggestedName = "script.fb") => Task.FromResult<IStorageFile?>(null);
        public Task ShowVariableDetailsAsync(string name, object? value) => Task.CompletedTask;
        public Task<string> ShowScriptInputAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        public Task<RemoteAttachEndpoint?> ShowRemoteAttachAsync() => Task.FromResult<RemoteAttachEndpoint?>(null);
    }
}