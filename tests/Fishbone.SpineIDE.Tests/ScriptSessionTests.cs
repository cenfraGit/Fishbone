using Fishbone.DebugClient;
using SpineIDE.Services;

namespace SpineIDE.Tests;

/// <summary>
/// <see cref="ScriptSession"/> runs, debugs and attaches to scripts for any front end, one at a
/// time, and reports what happens through events.
/// </summary>
public class ScriptSessionTests
{
    private static readonly Func<CancellationToken, Task<string>> NoInput = _ => Task.FromResult(string.Empty);

    private static (ScriptSession Session, FakeDebugSession Debug, List<string> Output) Create()
    {
        var debug = new FakeDebugSession();
        var session = new ScriptSession(new FakeFactory(debug));
        var output = new List<string>();
        session.Output += text => { lock (output) output.Add(text); };
        return (session, debug, output);
    }

    private static string Joined(List<string> output)
    {
        lock (output) return string.Concat(output);
    }

    [Fact]
    public async Task RunAsync_SendsOutputAndReturnsEnvironment()
    {
        var (session, _, output) = Create();

        var outcome = await session.RunAsync("println(\"hi\"); let x = 2;", null, NoInput);

        Assert.NotNull(outcome);
        Assert.Empty(outcome.Errors);
        Assert.Equal(2, outcome.Environment!.GetValue("x"));
        Assert.Equal("hi" + Environment.NewLine, Joined(output));
    }

    [Fact]
    public async Task RunAsync_Input_FlushesOutputFirstAndEchoesTheLine()
    {
        var (session, _, output) = Create();
        string? shownBeforeAsking = null;

        var outcome = await session.RunAsync("print(\"name? \"); let name = input();", null, _ =>
        {
            shownBeforeAsking = Joined(output);
            return Task.FromResult("Ada");
        });

        Assert.Equal("name? ", shownBeforeAsking);
        Assert.Equal("Ada", outcome!.Environment!.GetValue("name"));
        Assert.Equal("name? Ada" + Environment.NewLine, Joined(output));
    }

    [Fact]
    public async Task RunAsync_RuntimeError_ReturnsErrorWithLocation()
    {
        var (session, _, _) = Create();

        var outcome = await session.RunAsync("let x = 1;\nlet y = missing;", null, NoInput);

        var error = Assert.Single(outcome!.Errors);
        Assert.Equal(2, error.Line);
        Assert.Contains("missing", error.ExMessage);
        Assert.Null(outcome.Environment);
    }

    [Fact]
    public async Task RunAsync_ParseError_ReturnsEachError()
    {
        var (session, _, _) = Create();

        var outcome = await session.RunAsync("let = 1;\nlet = 2;", null, NoInput);

        Assert.Equal([1, 2], outcome!.Errors.Select(error => error.Line));
    }

    [Fact]
    public async Task RunAsync_RaisesStartedBeforeAnyOutput()
    {
        var (session, _, output) = Create();
        int outputWhenStarted = -1;
        session.Started += () => outputWhenStarted = output.Count;

        await session.RunAsync("println(1);", null, NoInput);

        Assert.Equal(0, outputWhenStarted);
    }

    [Fact]
    public async Task RunAsync_NewerRunCancelsTheOneBefore()
    {
        var (session, _, output) = Create();

        var first = session.RunAsync("while (true) { }", null, NoInput);
        await Task.Delay(100);
        var second = await session.RunAsync("let x = 1;", null, NoInput);

        Assert.Null((await first.WaitAsync(TimeSpan.FromSeconds(5)))?.Environment);
        Assert.Equal(1, second!.Environment!.GetValue("x"));
        // the cancelled run was replaced, so it doesn't report itself as cancelled
        Assert.DoesNotContain("cancelled", Joined(output));
    }

    [Fact]
    public async Task StopAsync_CancelsARun()
    {
        var (session, _, output) = Create();

        // stop only cancels a run that has started, so wait for it instead of guessing a delay
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Started += () => started.TrySetResult();
        var run = session.RunAsync("while (true) { }", null, NoInput);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync();
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(outcome!.Environment);
        Assert.Contains("[FishboneProgram] Execution cancelled.", Joined(output));
    }

    [Fact]
    public async Task DebugAsync_ConfiguresBreakpointsAndReportsEvents()
    {
        var (session, debug, output) = Create();
        var pauses = new List<bool>();
        session.Paused += (_, _, isProgramExit) => pauses.Add(isProgramExit);

        var run = session.DebugAsync(TempScript(), [3]);
        await debug.Configured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        debug.Raise(new FishboneDebugOutput("hello", FishboneDebugOutputCategory.Stdout));
        debug.Raise(new FishboneDebugPaused(Snapshot("breakpoint")));
        debug.Raise(new FishboneDebugPaused(Snapshot(FishbonePauseSnapshot.ProgramExitReason)));
        debug.Raise(new FishboneDebugTerminated(0));
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([3], debug.ConfiguredLines);
        Assert.Equal([false, true], pauses);
        Assert.Equal("hello", Joined(output));
        Assert.Empty(outcome!.Errors);
        Assert.True(debug.Disposed);
    }

    [Fact]
    public async Task DebugAsync_HostExitCode_IsAnError()
    {
        var (session, debug, _) = Create();

        var run = session.DebugAsync(TempScript(), []);
        await debug.Configured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        debug.Raise(new FishboneDebugTerminated(3));
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("3", Assert.Single(outcome!.Errors).ExMessage);
    }

    [Fact]
    public async Task UpdateBreakpointsAsync_SendsLinesWhileDebugging()
    {
        var (session, debug, _) = Create();

        var run = session.DebugAsync(TempScript(), []);
        await debug.Configured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var results = await session.UpdateBreakpointsAsync([5, 7]);
        debug.Raise(new FishboneDebugTerminated(0));
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal([5, 7], debug.UpdatedLines);
        Assert.Equal([5, 7], results!.Select(result => result.Line));
        // with no session, there's nothing to update
        Assert.Null(await session.UpdateBreakpointsAsync([1]));
    }

    [Fact]
    public async Task AttachAsync_OpensTheSourceAndUsesItsBreakpoints()
    {
        var (session, debug, _) = Create();
        FishboneDebugSource? opened = null;

        var run = session.AttachAsync("127.0.0.1", 4711, source =>
        {
            opened = source;
            return Task.FromResult<IReadOnlyList<int>>([2]);
        });
        await debug.Configured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        debug.Raise(new FishboneDebugFailed(new InvalidOperationException("host went away")));
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("remote.fb", opened!.Name);
        Assert.True(debug.ConnectedStoppingOnEntry);
        Assert.Equal([2], debug.ConfiguredLines);
        Assert.Equal("host went away", Assert.Single(outcome!.Errors).ExMessage);
    }

    [Fact]
    public async Task AttachAsync_HostExitCode_IsNotAnError()
    {
        // the host's exit code is the host's business, not the script's
        var (session, debug, _) = Create();

        var run = session.AttachAsync("127.0.0.1", 4711, _ => Task.FromResult<IReadOnlyList<int>>([]));
        await debug.Configured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        debug.Raise(new FishboneDebugTerminated(3));
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(outcome!.Errors);
    }

    [Fact]
    public async Task StopAsync_LeavesAnAttachedHostRunning()
    {
        var (session, debug, output) = Create();

        var run = session.AttachAsync("127.0.0.1", 4711, _ => Task.FromResult<IReadOnlyList<int>>([]));
        await debug.Configured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync();
        // a cancelled run would report itself as cancelled before this terminated event counts
        debug.Raise(new FishboneDebugTerminated(0));
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(debug.Stopped);
        Assert.Empty(outcome!.Errors);
        Assert.DoesNotContain("cancelled", Joined(output));
    }

    [Fact]
    public async Task DebugAsync_RunsFromTheScriptsFolder_AndGoesBack()
    {
        // a script reads files next to itself by relative path
        var (session, debug, _) = Create();
        string folder = Directory.CreateTempSubdirectory("fishbone-session-").FullName;
        string script = Path.Combine(folder, "script.fb");
        File.WriteAllText(script, "let x = 1;");
        string before = Directory.GetCurrentDirectory();
        try
        {
            var run = session.DebugAsync(script, []);
            await debug.Configured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            debug.Raise(new FishboneDebugTerminated(0));
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(Path.GetFullPath(folder), Path.GetFullPath(debug.ConnectedFrom!));
            Assert.Equal(before, Directory.GetCurrentDirectory());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string TempScript()
    {
        string path = Path.Combine(Path.GetTempPath(), $"session-{Guid.NewGuid():N}.fb");
        File.WriteAllText(path, "let x = 1;");
        return path;
    }

    private static FishbonePauseSnapshot Snapshot(string reason) =>
        new(1, reason, null, [new FishboneDebugFrame(1, "<script>", "test.fb", 2, 1, [])], null);

    private sealed class FakeFactory(FakeDebugSession session) : IFishboneDebugClientSessionFactory
    {
        public IFishboneDebugClientSession CreateLaunched(string scriptPath) => session;
        public IFishboneDebugClientSession CreateAttached(string host, int port)
        {
            session.Ownership = FishboneDebugSessionOwnership.Attached;
            return session;
        }
    }

    private sealed class FakeDebugSession : IFishboneDebugClientSession
    {
        public event EventHandler<FishboneDebugEvent>? EventReceived;
        public FishboneDebugSessionState State { get; private set; } = FishboneDebugSessionState.Starting;
        public FishboneDebugSessionOwnership Ownership { get; set; } = FishboneDebugSessionOwnership.Launched;
        public string? ConnectedFrom { get; private set; }
        public bool Stopped { get; private set; }
        public FishboneDebugSource? Source { get; private set; }
        public TaskCompletionSource Configured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<int>? ConfiguredLines { get; private set; }
        public IReadOnlyList<int>? UpdatedLines { get; private set; }
        public bool ConnectedStoppingOnEntry { get; private set; }
        public bool Disposed { get; private set; }

        public void Raise(FishboneDebugEvent debugEvent)
        {
            if (debugEvent is FishboneDebugTerminated)
                State = FishboneDebugSessionState.Completed;
            EventReceived?.Invoke(this, debugEvent);
        }

        public Task<FishboneDebugSource> ConnectAsync(bool stopOnEntry = false, CancellationToken cancellationToken = default)
        {
            ConnectedStoppingOnEntry = stopOnEntry;
            ConnectedFrom = Directory.GetCurrentDirectory();
            Source = new FishboneDebugSource("remote.fb", "remote.fb", 1, "let x = 1;", "text/plain");
            return Task.FromResult(Source);
        }

        public Task<IReadOnlyList<FishboneBreakpointResult>> ConfigureAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default)
        {
            ConfiguredLines = breakpoints;
            State = FishboneDebugSessionState.Running;
            Configured.TrySetResult();
            return Task.FromResult(Results(breakpoints));
        }

        public Task<IReadOnlyList<FishboneBreakpointResult>> SetBreakpointsAsync(IReadOnlyList<int> lines, CancellationToken cancellationToken = default)
        {
            UpdatedLines = lines;
            return Task.FromResult(Results(lines));
        }

        private static IReadOnlyList<FishboneBreakpointResult> Results(IReadOnlyList<int> lines) =>
            lines.Select(line => new FishboneBreakpointResult(line, true, null)).ToArray();

        public Task StartAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<FishboneDebugVariable>> GetVariablesAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FishboneDebugVariable>>([]);
        public Task<FishboneDebugImage> GetImageAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FishboneDebugImage([], 0, 0, [], []));
        public bool PauseAtEnd { get; set; }
        public Task<FishboneDebugVariable> EvaluateAsync(string expression, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FishboneDebugVariable(expression, "", null, null, null, null));
        public Task<FishboneDebugCompletions?> GetCompletionsAsync(string expression, int caret, CancellationToken cancellationToken = default) =>
            Task.FromResult<FishboneDebugCompletions?>(null);
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepIntoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOverAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
