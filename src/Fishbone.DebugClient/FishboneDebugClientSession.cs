using System.Collections.Immutable;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using OmniSharp.Extensions.DebugAdapter.Client;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using DapStackFrame = OmniSharp.Extensions.DebugAdapter.Protocol.Models.StackFrame;

namespace Fishbone.DebugClient;

public sealed class FishboneDebugClientSession : IFishboneDebugClientSession
{
    public const long ThreadId = 1;
    private readonly IFishboneDapHostLocator _hostLocator;
    private readonly Channel<ProtocolEvent> _events = Channel.CreateUnbounded<ProtocolEvent>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private Process? _process;
    private TcpClient? _tcpClient;
    private DebugAdapterClient? _client;
    private Task? _eventPump;
    private int _generation;
    private int? _exitCode;
    private FishboneDebugException? _lastException;
    private bool _disposed;

    public FishboneDebugClientSession(string scriptPath, IFishboneDapHostLocator hostLocator)
    {
        ScriptPath = Path.GetFullPath(scriptPath);
        _hostLocator = hostLocator;
    }

    public event EventHandler<FishboneDebugEvent>? EventReceived;
    public FishboneDebugSessionState State { get; private set; } = FishboneDebugSessionState.Starting;
    public string ScriptPath { get; }

    public async Task StartAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            PublishState(FishboneDebugSessionState.Starting);
            _process = StartHost();
            _process.Exited += OnProcessExited;
            string endpoint = await ReadEndpointAsync(_process, linked.Token).ConfigureAwait(false);
            (string host, int port) = ParseEndpoint(endpoint);

            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            NetworkStream stream = _tcpClient.GetStream();
            _client = DebugAdapterClient.Create(options =>
            {
                options.WithInput(stream).WithOutput(stream);
                options.ClientId = "spineide";
                options.ClientName = "SpineIDE";
                options.AdapterId = "fishbone";
                options.LinesStartAt1 = true;
                options.ColumnsStartAt1 = true;
                options.PathFormat = PathFormat.Path;
                options.SupportsVariableType = true;
                options.SupportsVariablePaging = true;
                options.OnStopped(value => Enqueue(new StoppedProtocolEvent(value)));
                options.OnOutput(value => Enqueue(new OutputProtocolEvent(value)));
                options.OnContinued(_ => Enqueue(new ContinuedProtocolEvent()));
                options.OnTerminated(_ => Enqueue(new TerminatedProtocolEvent()));
                options.OnExited(value => _exitCode = checked((int)value.ExitCode));
            });
            _eventPump = PumpEventsAsync(_lifetime.Token);
            await _client.Initialize(linked.Token).ConfigureAwait(false);
            await _client.Attach(new AttachRequestArguments(), linked.Token).ConfigureAwait(false);
            await _client.SetExceptionBreakpoints(new SetExceptionBreakpointsArguments
            {
                Filters = new Container<string>("all")
            }, linked.Token).ConfigureAwait(false);
            await SetBreakpointsAsync(breakpoints, linked.Token).ConfigureAwait(false);
            await _client.RequestConfigurationDone(new ConfigurationDoneArguments(), linked.Token).ConfigureAwait(false);
            PublishState(FishboneDebugSessionState.Running);
        }
        catch (Exception exception)
        {
            PublishState(FishboneDebugSessionState.Faulted);
            Publish(new FishboneDebugFailed(exception));
            await DisposeTransportAsync(killProcess: true).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<FishboneBreakpointResult>> SetBreakpointsAsync(IReadOnlyList<int> lines, CancellationToken cancellationToken = default)
    {
        DebugAdapterClient client = RequireClient();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var response = await client.SetBreakpoints(new SetBreakpointsArguments
            {
                Source = new Source { Name = Path.GetFileName(ScriptPath), Path = ScriptPath },
                Breakpoints = new Container<SourceBreakpoint>(lines.Distinct().Order().Select(line => new SourceBreakpoint { Line = line }))
            }, cancellationToken).ConfigureAwait(false);
            var results = (response.Breakpoints ?? []).Select((breakpoint, index) => new FishboneBreakpointResult(
                breakpoint.Line ?? (index < lines.Count ? lines[index] : 0), breakpoint.Verified, breakpoint.Message)).ToArray();
            return results;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<IReadOnlyList<FishboneDebugVariable>> GetVariablesAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default)
    {
        if (State != FishboneDebugSessionState.Paused || handle.Generation != Volatile.Read(ref _generation))
            throw new InvalidOperationException("The variable reference belongs to an inactive pause.");
        var response = await RequireClient().RequestVariables(new VariablesArguments { VariablesReference = handle.Reference }, cancellationToken).ConfigureAwait(false);
        return (response.Variables ?? []).Select(variable => MapVariable(variable, handle.Generation)).ToArray();
    }

    public Task ContinueAsync(CancellationToken cancellationToken = default) => ResumeAsync(
        client => client.RequestContinue(new ContinueArguments { ThreadId = ThreadId }, cancellationToken));

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        RequireClient().RequestPause(new PauseArguments { ThreadId = ThreadId }, cancellationToken);

    public Task StepIntoAsync(CancellationToken cancellationToken = default) => ResumeAsync(
        client => client.RequestStepIn(new StepInArguments { ThreadId = ThreadId }, cancellationToken));

    public Task StepOverAsync(CancellationToken cancellationToken = default) => ResumeAsync(
        client => client.RequestNext(new NextArguments { ThreadId = ThreadId }, cancellationToken));

    public Task StepOutAsync(CancellationToken cancellationToken = default) => ResumeAsync(
        client => client.RequestStepOut(new StepOutArguments { ThreadId = ThreadId }, cancellationToken));

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (State is FishboneDebugSessionState.Completed or FishboneDebugSessionState.Faulted)
            return;
        PublishState(FishboneDebugSessionState.Stopping);
        try
        {
            if (_client is not null)
                await _client.RequestTerminate(new TerminateArguments(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Process cleanup below is authoritative when the protocol transport is already gone.
        }
        await WaitForExitOrKillAsync().ConfigureAwait(false);
    }

    private Process StartHost()
    {
        FishboneDapHostCommand host = _hostLocator.Locate();
        var startInfo = new ProcessStartInfo(host.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(ScriptPath) ?? AppContext.BaseDirectory
        };
        foreach (string argument in host.PrefixArguments) startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("0");
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("Could not start fishbone-dap.");
        _ = PumpStandardErrorAsync(process, _lifetime.Token);
        return process;
    }

    private static async Task<string> ReadEndpointAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string? line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(line) ? throw new InvalidOperationException("fishbone-dap did not publish an endpoint.") : line;
    }

    private async Task PumpStandardErrorAsync(Process process, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return;
            Publish(new FishboneDebugOutput(line + Environment.NewLine, FishboneDebugOutputCategory.Stderr));
        }
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        int separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || !int.TryParse(endpoint[(separator + 1)..], out int port))
            throw new FormatException($"Invalid fishbone-dap endpoint: {endpoint}");
        return (endpoint[..separator], port);
    }

    private async Task ResumeAsync(Func<DebugAdapterClient, Task> request)
    {
        if (State != FishboneDebugSessionState.Paused) return;
        await request(RequireClient()).ConfigureAwait(false);
    }

    private async Task PumpEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ProtocolEvent item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (item)
                {
                    case StoppedProtocolEvent stopped:
                        await PublishPauseAsync(stopped.Value, cancellationToken).ConfigureAwait(false);
                        break;
                    case OutputProtocolEvent output:
                        Publish(new FishboneDebugOutput(output.Value.Output, output.Value.Category == OutputEventCategory.StandardError
                            ? FishboneDebugOutputCategory.Stderr : FishboneDebugOutputCategory.Stdout));
                        break;
                    case ContinuedProtocolEvent:
                        PublishState(FishboneDebugSessionState.Running);
                        Publish(new FishboneDebugContinued());
                        break;
                    case TerminatedProtocolEvent:
                        if (_exitCode is not null and not 0 && _lastException is not null)
                            Publish(new FishboneDebugFailed(new InvalidOperationException(
                                $"{_lastException.Id}: {_lastException.Description}")));
                        PublishState(FishboneDebugSessionState.Completed);
                        Publish(new FishboneDebugTerminated(_exitCode));
                        break;
                    case TransportFailedProtocolEvent failed:
                        PublishState(FishboneDebugSessionState.Faulted);
                        Publish(new FishboneDebugFailed(failed.Exception));
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PublishState(FishboneDebugSessionState.Faulted);
            Publish(new FishboneDebugFailed(exception));
        }
    }

    private async Task PublishPauseAsync(StoppedEvent stopped, CancellationToken cancellationToken)
    {
        int generation = Interlocked.Increment(ref _generation);
        long threadId = stopped.ThreadId ?? ThreadId;
        var stack = await RequireClient().RequestStackTrace(new StackTraceArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
        var frames = ImmutableArray.CreateBuilder<FishboneDebugFrame>();
        foreach (DapStackFrame frame in stack.StackFrames ?? [])
        {
            var scopesResponse = await RequireClient().RequestScopes(new ScopesArguments { FrameId = frame.Id }, cancellationToken).ConfigureAwait(false);
            var scopes = ImmutableArray.CreateBuilder<FishboneDebugScope>();
            foreach (Scope scope in scopesResponse.Scopes)
            {
                var handle = new FishboneVariableHandle(generation, scope.VariablesReference);
                var variablesResponse = await RequireClient().RequestVariables(new VariablesArguments { VariablesReference = scope.VariablesReference }, cancellationToken).ConfigureAwait(false);
                scopes.Add(new FishboneDebugScope(scope.Name, handle,
                    (variablesResponse.Variables ?? []).Select(variable => MapVariable(variable, generation)).ToImmutableArray()));
            }
            frames.Add(new FishboneDebugFrame(frame.Id, frame.Name ?? "<script>", frame.Source?.Path,
                frame.Line, frame.Column, scopes.ToImmutable()));
        }

        FishboneDebugException? exception = null;
        if (stopped.Reason == StoppedEventReason.Exception)
        {
            var info = await RequireClient().RequestExceptionInfo(new ExceptionInfoArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
            exception = new FishboneDebugException(info.ExceptionId, info.Description);
            _lastException = exception;
        }

        PublishState(FishboneDebugSessionState.Paused);
        Publish(new FishboneDebugPaused(new FishbonePauseSnapshot(generation, stopped.Reason.ToString(), stopped.Description,
            frames.ToImmutable(), exception)));
    }

    private static FishboneDebugVariable MapVariable(Variable variable, int generation) => new(
        variable.Name, variable.Value, variable.Type,
        variable.VariablesReference == 0 ? null : new FishboneVariableHandle(generation, variable.VariablesReference),
        variable.NamedVariables, variable.IndexedVariables);

    private void Enqueue(ProtocolEvent item) => _events.Writer.TryWrite(item);
    private void Publish(FishboneDebugEvent item) => EventReceived?.Invoke(this, item);
    private void PublishState(FishboneDebugSessionState state)
    {
        State = state;
        Publish(new FishboneDebugStateChanged(state));
    }

    private DebugAdapterClient RequireClient() => _client ?? throw new InvalidOperationException("The DAP client is not connected.");

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_process is { } process) _exitCode = process.ExitCode;
        if (State is FishboneDebugSessionState.Completed or FishboneDebugSessionState.Stopping) return;
        Enqueue(new TransportFailedProtocolEvent(new IOException("fishbone-dap exited unexpectedly.")));
    }

    private async Task WaitForExitOrKillAsync()
    {
        if (_process is null || _process.HasExited) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    private async Task DisposeTransportAsync(bool killProcess)
    {
        if (killProcess) await WaitForExitOrKillAsync().ConfigureAwait(false);
        _client?.Dispose();
        _tcpClient?.Dispose();
        _process?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (State is FishboneDebugSessionState.Running or FishboneDebugSessionState.Paused)
            await StopAsync().ConfigureAwait(false);
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _events.Writer.TryComplete();
        if (_eventPump is not null)
        {
            try { await _eventPump.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        await DisposeTransportAsync(killProcess: true).ConfigureAwait(false);
        _requestGate.Dispose();
        _lifetime.Dispose();
    }

    private abstract record ProtocolEvent;
    private sealed record StoppedProtocolEvent(StoppedEvent Value) : ProtocolEvent;
    private sealed record OutputProtocolEvent(OutputEvent Value) : ProtocolEvent;
    private sealed record ContinuedProtocolEvent : ProtocolEvent;
    private sealed record TerminatedProtocolEvent : ProtocolEvent;
    private sealed record TransportFailedProtocolEvent(Exception Exception) : ProtocolEvent;
}