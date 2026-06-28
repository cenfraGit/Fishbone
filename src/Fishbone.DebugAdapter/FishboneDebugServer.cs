using System.Net;
using System.Net.Sockets;
using Fishbone.Core;
using Fishbone.Debugging;
using Fishbone.Engine;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace Fishbone.DebugAdapter;

public sealed record FishboneDebugServerResult(
    int ExitCode,
    FishboneEnvironment? Environment,
    Exception? Error,
    bool WasCancelled);

public static class FishboneDebugServer
{
    public static Task<FishboneDebugServerSession> StartAsync(
        FishboneDebugServerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SourceName))
            throw new ArgumentException("A source name is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.SourceIdentity))
            throw new ArgumentException("A stable source identity is required.", nameof(options));

        var listener = new TcpListener(options.ListenEndpoint);
        listener.Start(1);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        return Task.FromResult(new FishboneDebugServerSession(options, listener, endpoint, cancellationToken));
    }
}

public sealed class FishboneDebugServerSession : IAsyncDisposable
{
    private readonly FishboneDebugServerOptions _options;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _sync = new();
    private readonly TaskCompletionSource _clientConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FishboneDebugAdapterSession? _adapterSession;
    private bool _stopRequested;
    private bool _clientAccepted;
    private bool _disposed;

    internal FishboneDebugServerSession(
        FishboneDebugServerOptions options,
        TcpListener listener,
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        _options = options;
        _listener = listener;
        Endpoint = endpoint;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Completion = RunAsync(_lifetime.Token);
    }

    public IPEndPoint Endpoint { get; }
    public Task<FishboneDebugServerResult> Completion { get; }

    /// <summary>
    /// Completes when a debug client attaches. If the session ends before any client connects
    /// (cancelled or stopped), this task is cancelled
    /// </summary>
    public Task ClientConnected => _clientConnected.Task;

    public async Task StopAsync()
    {
        _stopRequested = true;
        FishboneDebugAdapterSession? adapter;
        lock (_sync) adapter = _adapterSession;
        if (adapter is not null)
            adapter.Stop();
        if (!_clientAccepted)
        {
            _listener.Stop();
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
        await Completion.ConfigureAwait(false);
    }

    private async Task<FishboneDebugServerResult> RunAsync(CancellationToken cancellationToken)
    {
        FishboneEnvironment? environment = null;
        Exception? executionError = null;
        bool executionCancelled = false;
        try
        {
            string source = _options.SourceCode;
            int lineCount = source.Count(character => character == '\n') + 1;
            FishboneConfiguration configuration = CloneConfiguration(_options.Configuration);
            using var coordinator = new BreakpointCoordinator(_options.SourceIdentity, cancellationToken);
            FishboneDebugAdapterSession? session = null;
            if (_options.RedirectOutput)
            {
                configuration.AddBuiltIn("print", new Action<object?>(value =>
                    session?.WriteOutput(value?.ToString() ?? string.Empty)));
                configuration.AddBuiltIn("println", new Action<object?>(value =>
                    session?.WriteOutput((value?.ToString() ?? string.Empty) + Environment.NewLine)));
            }

            using var activeSession = new FishboneDebugAdapterSession(
                coordinator,
                _options.SourceIdentity,
                _options.SourceName,
                source,
                lineCount,
                token =>
                {
                    try
                    {
                        environment = FishboneEngine.Run(source, configuration, token, coordinator);
                    }
                    catch (OperationCanceledException)
                    {
                        executionCancelled = true;
                        throw;
                    }
                    catch (Exception exception)
                    {
                        executionError = exception;
                        throw;
                    }
                    return Task.CompletedTask;
                });
            session = activeSession;
            lock (_sync) _adapterSession = activeSession;
            using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(() =>
                _ = Task.Run(activeSession.Stop));

            using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _clientAccepted = true;
            _clientConnected.TrySetResult();
            _listener.Stop();
            using NetworkStream stream = client.GetStream();
            using DebugAdapterServer server = DebugAdapterServer.Create(options =>
            {
                options.WithInput(stream).WithOutput(stream).AddHandler(activeSession);
                options.Capabilities = CreateCapabilities();
            });
            activeSession.AttachServer(server, CancellationToken.None);

            Task initialize = server.Initialize(cancellationToken);
            Task disconnected = MonitorDisconnectAsync(client, cancellationToken);
            Task first = await Task.WhenAny(initialize, disconnected).ConfigureAwait(false);
            if (first == disconnected)
            {
                activeSession.Stop();
                int disconnectedExit = await activeSession.Completion.ConfigureAwait(false);
                return new FishboneDebugServerResult(disconnectedExit, environment, executionError, executionCancelled || _stopRequested);
            }

            await initialize.ConfigureAwait(false);
            first = await Task.WhenAny(activeSession.Completion, disconnected).ConfigureAwait(false);
            if (first == disconnected && !activeSession.IsDetached)
                activeSession.Stop();

            int exitCode = await activeSession.Completion.ConfigureAwait(false);
            if (!activeSession.IsDetached && !disconnected.IsCompleted)
                await Task.WhenAny(
                    disconnected,
                    Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)).ConfigureAwait(false);
            return new FishboneDebugServerResult(exitCode, environment, executionError, executionCancelled || _stopRequested);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new FishboneDebugServerResult(1, environment, executionError, true);
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
            return new FishboneDebugServerResult(1, environment, executionError, true);
        }
        catch (Exception exception)
        {
            return new FishboneDebugServerResult(1, environment, executionError ?? exception, executionCancelled);
        }
        finally
        {
            // if we exited without a client ever attaching, release anyone awaiting ClientConnected
            _clientConnected.TrySetCanceled();
            lock (_sync) _adapterSession = null;
            _listener.Stop();
        }
    }

    private static Capabilities CreateCapabilities() => new()
    {
        SupportsConfigurationDoneRequest = true,
        SupportsTerminateRequest = true,
        SupportTerminateDebuggee = true,
        SupportsExceptionInfoRequest = true,
        SupportsLoadedSourcesRequest = true,
        ExceptionBreakpointFilters = new Container<ExceptionBreakpointsFilter>(new ExceptionBreakpointsFilter
        {
            Filter = "all",
            Label = "All Runtime Exceptions",
            Default = true
        })
    };

    private static FishboneConfiguration CloneConfiguration(FishboneConfiguration source)
    {
        var clone = new FishboneConfiguration(injectDefaults: false)
        {
            EnableImports = source.EnableImports,
            EnableLoops = source.EnableLoops,
            EnableFunctionDeclaration = source.EnableFunctionDeclaration,
            EnableFunctionCall = source.EnableFunctionCall
        };
        foreach (var builtIn in source.BuiltIns)
            clone.BuiltIns[builtIn.Key] = builtIn.Value;
        foreach (var value in source.Values)
            clone.Values[value.Key] = value.Value;
        return clone;
    }

    private static async Task MonitorDisconnectAsync(TcpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (client.Client.Poll(1000, SelectMode.SelectRead) && client.Client.Available == 0)
                return;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (!Completion.IsCompleted)
            await StopAsync().ConfigureAwait(false);
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }
}