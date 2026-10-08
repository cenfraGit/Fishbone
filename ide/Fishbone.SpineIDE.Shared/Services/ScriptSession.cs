using Fishbone;
using Fishbone.DebugClient;
using SpineIDE.Panels;

namespace SpineIDE.Services;

/// <summary>
/// What a run, debug session or attach ended with. <see cref="Environment"/> is set only when a
/// run finished normally and wasn't replaced by a newer one. <see cref="Configuration"/> comes with it,
/// so a front end can show the final values with the same visualizers the run had.
/// </summary>
public sealed record ScriptRunOutcome(
    FishboneEnvironment? Environment,
    IReadOnlyList<ScriptExecutionError> Errors,
    FishboneConfiguration? Configuration = null);

/// <summary>
/// Runs, debugs and attaches to scripts for a SpineIDE front end, one at a time: starting a new one
/// cancels the one before. Events are raised on whatever thread they happen on, so each front end
/// moves them to its own UI thread, in order.
/// </summary>
public sealed class ScriptSession
{
    private readonly IFishboneDebugClientSessionFactory _debugSessionFactory;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly SemaphoreSlim _breakpointSyncGate = new(1, 1);
    private CancellationTokenSource? _scriptCTS;
    private IFishboneDebugClientSession? _debugSession;
    private int _breakpointRevision;
    private int _executionVersion;

    public ScriptSession(IFishboneDebugClientSessionFactory debugSessionFactory)
    {
        _debugSessionFactory = debugSessionFactory;
    }

    /// <summary>A run, debug session or attach took over. The previous output and errors are stale.</summary>
    public event Action? Started;

    public event Action<string>? Output;

    public event Action<FishboneDebugSessionState>? StateChanged;

    /// <summary>
    /// The debugger paused. The flag is true for the pause after stepping off the end of the script,
    /// which only shows the final values.
    /// </summary>
    public event Action<FishbonePauseSnapshot, IFishboneDebugClientSession, bool>? Paused;

    public event Action? Continued;

    // --------------------------------------------------------------------------------
    // starting
    // --------------------------------------------------------------------------------

    /// <summary>
    /// Runs a script without the debugger. <paramref name="readInput"/> answers the script's
    /// <c>input()</c>, and is called on the script's thread. Returns null if a newer run replaced
    /// this one before it started.
    /// </summary>
    public Task<ScriptRunOutcome?> RunAsync(string code, string? directory, Func<CancellationToken, Task<string>> readInput) =>
        ExecuteAsync(directory, (version, token) => RunScriptAsync(code, readInput, version, token));

    /// <summary>
    /// Debugs a saved script in the debug host. <paramref name="breakpointsApplied"/> gets the
    /// adapter's answer for the starting breakpoints.
    /// </summary>
    public Task<ScriptRunOutcome?> DebugAsync(
        string scriptPath,
        IReadOnlyList<int> breakpoints,
        Action<IReadOnlyList<FishboneBreakpointResult>> breakpointsApplied) =>
        ExecuteAsync(Path.GetDirectoryName(scriptPath), (_, token) =>
            DebugScriptAsync(scriptPath, breakpoints, breakpointsApplied, token));

    /// <summary>
    /// Attaches to a running debug server. <paramref name="openSource"/> shows the remote script and
    /// returns the breakpoints to start with.
    /// </summary>
    public Task<ScriptRunOutcome?> AttachAsync(
        string host,
        int port,
        Func<FishboneDebugSource, Task<IReadOnlyList<int>>> openSource,
        Action<IReadOnlyList<FishboneBreakpointResult>> breakpointsApplied) =>
        ExecuteAsync(null, (_, token) => AttachScriptAsync(host, port, openSource, breakpointsApplied, token));

    private async Task<ScriptRunOutcome?> ExecuteAsync(
        string? directory,
        Func<int, CancellationToken, Task<ScriptRunOutcome>> execute)
    {
        int executionVersion = Interlocked.Increment(ref _executionVersion);
        _scriptCTS?.Cancel();
        await _executionGate.WaitAsync();

        try
        {
            if (executionVersion != Volatile.Read(ref _executionVersion))
                return null;

            using var currentCTS = new CancellationTokenSource();
            _scriptCTS = currentCTS;
            Started?.Invoke();

            string currentDirectory = Directory.GetCurrentDirectory();
            try
            {
                if (directory is not null && Directory.Exists(directory))
                    Directory.SetCurrentDirectory(directory);

                var outcome = await execute(executionVersion, currentCTS.Token);
                if (outcome.Environment is not null && !IsCurrent(executionVersion, currentCTS.Token))
                    outcome = outcome with { Environment = null };
                return outcome;
            }
            catch (OperationCanceledException)
            {
                if (executionVersion == Volatile.Read(ref _executionVersion))
                    Output?.Invoke("[FishboneProgram] Execution cancelled." + Environment.NewLine);
                return new ScriptRunOutcome(null, []);
            }
            catch (Exception ex)
            {
                return new ScriptRunOutcome(null,
                    executionVersion == Volatile.Read(ref _executionVersion) ? ScriptExecutionError.From(ex) : []);
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

    private bool IsCurrent(int executionVersion, CancellationToken cancellationToken) =>
        executionVersion == Volatile.Read(ref _executionVersion) && !cancellationToken.IsCancellationRequested;

    // --------------------------------------------------------------------------------
    // running
    // --------------------------------------------------------------------------------

    private async Task<ScriptRunOutcome> RunScriptAsync(
        string code,
        Func<CancellationToken, Task<string>> readInput,
        int executionVersion,
        CancellationToken cancellationToken)
    {
        var outputBuffer = new ScriptOutputBuffer();
        var configuration = SpineConfiguration.Create(
            outputBuffer.Append,
            outputBuffer.AppendLine,
            () => ReadInput(readInput, outputBuffer, executionVersion, cancellationToken));

        Task<ScriptRunOutcome> executionTask = Task.Run(
            () =>
            {
                try
                {
                    return new ScriptRunOutcome(
                        FishboneProgram.Run(code, configuration, cancellationToken: cancellationToken),
                        [],
                        configuration);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new ScriptRunOutcome(null, ScriptExecutionError.From(ex));
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

    // runs on the script's thread, which waits here for the answer
    private string ReadInput(
        Func<CancellationToken, Task<string>> readInput,
        ScriptOutputBuffer outputBuffer,
        int executionVersion,
        CancellationToken cancellationToken)
    {
        if (!IsCurrent(executionVersion, cancellationToken))
            throw new OperationCanceledException(cancellationToken);

        // the prompt the script printed has to show before the question
        FlushOutput(outputBuffer, executionVersion, cancellationToken);
        string value = readInput(cancellationToken).WaitAsync(cancellationToken).GetAwaiter().GetResult();

        if (!IsCurrent(executionVersion, cancellationToken))
            throw new OperationCanceledException(cancellationToken);

        outputBuffer.AppendLine(value);
        FlushOutput(outputBuffer, executionVersion, cancellationToken);
        return value;
    }

    private void FlushOutput(ScriptOutputBuffer outputBuffer, int executionVersion, CancellationToken cancellationToken)
    {
        string output = outputBuffer.DrainPending();
        if (output.Length > 0 && IsCurrent(executionVersion, cancellationToken))
            Output?.Invoke(output);
    }

    // --------------------------------------------------------------------------------
    // debugging
    // --------------------------------------------------------------------------------

    private async Task<ScriptRunOutcome> DebugScriptAsync(
        string scriptPath,
        IReadOnlyList<int> breakpoints,
        Action<IReadOnlyList<FishboneBreakpointResult>> breakpointsApplied,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ScriptRunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        IFishboneDebugClientSession session = _debugSessionFactory.CreateLaunched(scriptPath);
        _debugSession = session;
        session.EventReceived += OnDebugEventReceived;

        void HandleCompletion(object? sender, FishboneDebugEvent debugEvent)
        {
            switch (debugEvent)
            {
                case FishboneDebugTerminated terminated:
                    completion.TrySetResult(terminated.ExitCode is null or 0
                        ? new ScriptRunOutcome(null, [])
                        : new ScriptRunOutcome(null, ScriptExecutionError.From(
                            new InvalidOperationException($"fishbone-dap exited with code {terminated.ExitCode}."))));
                    break;
                case FishboneDebugFailed failed:
                    completion.TrySetResult(new ScriptRunOutcome(null, ScriptExecutionError.From(failed.Exception)));
                    break;
            }
        }

        session.EventReceived += HandleCompletion;
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await session.ConnectAsync(stopOnEntry: false, cancellationToken);
            breakpointsApplied(await session.ConfigureAsync(breakpoints, cancellationToken));
            return await completion.Task;
        }
        finally
        {
            await EndDebugSessionAsync(session, HandleCompletion);
        }
    }

    private async Task<ScriptRunOutcome> AttachScriptAsync(
        string host,
        int port,
        Func<FishboneDebugSource, Task<IReadOnlyList<int>>> openSource,
        Action<IReadOnlyList<FishboneBreakpointResult>> breakpointsApplied,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ScriptRunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        IFishboneDebugClientSession session = _debugSessionFactory.CreateAttached(host, port);
        _debugSession = session;
        session.EventReceived += OnDebugEventReceived;

        void HandleCompletion(object? sender, FishboneDebugEvent debugEvent)
        {
            if (debugEvent is FishboneDebugTerminated)
                completion.TrySetResult(new ScriptRunOutcome(null, []));
            else if (debugEvent is FishboneDebugFailed failed)
                completion.TrySetResult(new ScriptRunOutcome(null, ScriptExecutionError.From(failed.Exception)));
        }

        session.EventReceived += HandleCompletion;
        using CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            FishboneDebugSource source = await session.ConnectAsync(stopOnEntry: true, cancellationToken);
            IReadOnlyList<int> breakpoints = await openSource(source);
            breakpointsApplied(await session.ConfigureAsync(breakpoints, cancellationToken));
            return await completion.Task;
        }
        finally
        {
            await EndDebugSessionAsync(session, HandleCompletion);
        }
    }

    private async Task EndDebugSessionAsync(IFishboneDebugClientSession session, EventHandler<FishboneDebugEvent> handleCompletion)
    {
        session.EventReceived -= handleCompletion;
        session.EventReceived -= OnDebugEventReceived;
        await session.DisposeAsync();
        if (ReferenceEquals(_debugSession, session))
            _debugSession = null;
        StateChanged?.Invoke(FishboneDebugSessionState.Completed);
    }

    private void OnDebugEventReceived(object? sender, FishboneDebugEvent debugEvent)
    {
        switch (debugEvent)
        {
            case FishboneDebugStateChanged state:
                StateChanged?.Invoke(state.State);
                break;
            case FishboneDebugOutput output:
                Output?.Invoke(output.Text);
                break;
            case FishboneDebugPaused paused when sender is IFishboneDebugClientSession session:
                bool isProgramExit = string.Equals(
                    paused.Snapshot.Reason, FishbonePauseSnapshot.ProgramExitReason, StringComparison.OrdinalIgnoreCase);
                Paused?.Invoke(paused.Snapshot, session, isProgramExit);
                break;
            case FishboneDebugContinued:
                Continued?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Sends the editor's breakpoints to the running debug session. Returns null when there's no
    /// session, or when a newer update replaced this one.
    /// </summary>
    public async Task<IReadOnlyList<FishboneBreakpointResult>?> UpdateBreakpointsAsync(IReadOnlyList<int> lines)
    {
        IFishboneDebugClientSession? session = _debugSession;
        int requestedRevision = Interlocked.Increment(ref _breakpointRevision);
        if (session is null || session.State is FishboneDebugSessionState.Completed or FishboneDebugSessionState.Faulted)
            return null;

        await _breakpointSyncGate.WaitAsync();
        try
        {
            if (requestedRevision != Volatile.Read(ref _breakpointRevision))
                return null;
            IReadOnlyList<FishboneBreakpointResult> results = await session.SetBreakpointsAsync(lines);
            return requestedRevision == Volatile.Read(ref _breakpointRevision) ? results : null;
        }
        finally
        {
            _breakpointSyncGate.Release();
        }
    }

    // --------------------------------------------------------------------------------
    // controlling
    // --------------------------------------------------------------------------------

    public Task ContinueAsync() => _debugSession?.ContinueAsync() ?? Task.CompletedTask;

    public Task PauseAsync() => _debugSession?.PauseAsync() ?? Task.CompletedTask;

    public Task StepIntoAsync() => _debugSession?.StepIntoAsync() ?? Task.CompletedTask;

    public Task StepOverAsync() => _debugSession?.StepOverAsync() ?? Task.CompletedTask;

    public Task StepOutAsync() => _debugSession?.StepOutAsync() ?? Task.CompletedTask;

    /// <summary>Stops the debug session or the run. An attached host keeps running.</summary>
    public async Task StopAsync()
    {
        IFishboneDebugClientSession? session = _debugSession;
        if (session is not null)
            await session.StopAsync();
        if (session?.Ownership != FishboneDebugSessionOwnership.Attached)
            _scriptCTS?.Cancel();
    }
}
