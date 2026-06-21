using System.Net;
using System.Net.Sockets;
using Fishbone.DebugAdapter;
using Fishbone.Debugging;
using Fishbone.Engine;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Server;

namespace Fishbone.Dap;

public sealed class FishboneDapHost
{
    public async Task<int> RunAsync(string scriptPath, int port, TextWriter endpointWriter, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(scriptPath);
        string source = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        int lineCount = source.Count(character => character == '\n') + 1;
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var coordinator = new BreakpointCoordinator(fullPath, sessionCancellation.Token);
        var configuration = new FishboneConfiguration();
        FishboneDebugAdapterSession? session = null;
        configuration.RegisterBuiltIn("print", new Action<object?>(value => session!.WriteOutput(value?.ToString() ?? string.Empty)));
        configuration.RegisterBuiltIn("println", new Action<object?>(value => session!.WriteOutput((value?.ToString() ?? string.Empty) + Environment.NewLine)));
        using var activeSession = new FishboneDebugAdapterSession(
            coordinator,
            fullPath,
            lineCount,
            token =>
            {
                FishboneEngine.Run(source, configuration, token, coordinator);
                return Task.CompletedTask;
            });
        session = activeSession;
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(1);
        int actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        await endpointWriter.WriteLineAsync($"127.0.0.1:{actualPort}").ConfigureAwait(false);
        await endpointWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        listener.Stop();
        using NetworkStream stream = client.GetStream();
        using DebugAdapterServer server = DebugAdapterServer.Create(options =>
        {
            options.WithInput(stream).WithOutput(stream).AddHandler(activeSession);
            options.Capabilities = new Capabilities
            {
                SupportsConfigurationDoneRequest = true,
                SupportsTerminateRequest = true,
                SupportTerminateDebuggee = true,
                SupportsExceptionInfoRequest = true,
                ExceptionBreakpointFilters = new Container<ExceptionBreakpointsFilter>(new ExceptionBreakpointsFilter
                {
                    Filter = "all",
                    Label = "All Runtime Exceptions",
                    Default = true
                })
            };
        });
        activeSession.AttachServer(server, sessionCancellation.Token);

        Task initialize = server.Initialize(sessionCancellation.Token);
        Task disconnected = MonitorDisconnectAsync(client, sessionCancellation.Token);
        Task first = await Task.WhenAny(initialize, disconnected).ConfigureAwait(false);
        if (first == disconnected)
        {
            activeSession.Stop();
            return await activeSession.Completion.ConfigureAwait(false);
        }

        await initialize.ConfigureAwait(false);
        first = await Task.WhenAny(activeSession.Completion, disconnected).ConfigureAwait(false);
        if (first == disconnected && !activeSession.IsDetached)
            activeSession.Stop();

        int exitCode = await activeSession.Completion.ConfigureAwait(false);
        if (!activeSession.IsDetached && !disconnected.IsCompleted)
            await Task.WhenAny(disconnected, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)).ConfigureAwait(false);
        await sessionCancellation.CancelAsync().ConfigureAwait(false);
        return exitCode;
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
}