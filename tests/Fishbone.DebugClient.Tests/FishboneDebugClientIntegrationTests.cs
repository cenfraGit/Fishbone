using Fishbone.DebugClient;
using Fishbone.DebugAdapter;
using System.Net;
using System.Threading.Channels;

namespace Fishbone.DebugClient.Tests;

public class FishboneDebugClientIntegrationTests
{
    [Fact]
    public async Task SessionLaunchesHostAttachesInspectsAndContinues()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"fishbone-client-{Guid.NewGuid():N}.fb");
        await File.WriteAllTextAsync(scriptPath, "let x = 1;\nprintln(x);\nx = x + 1;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = new FishboneDebugClientSession(scriptPath, new FishboneDapHostLocator(AppContext.BaseDirectory));
        var paused = new TaskCompletionSource<FishbonePauseSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new List<string>();
        session.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused value) paused.TrySetResult(value.Snapshot);
            if (debugEvent is FishboneDebugTerminated) terminated.TrySetResult();
            if (debugEvent is FishboneDebugOutput valueOutput) output.Add(valueOutput.Text);
        };

        await session.StartAsync([2], timeout.Token);
        FishbonePauseSnapshot snapshot = await paused.Task.WaitAsync(timeout.Token);

        FishboneDebugScope visible = snapshot.Frames[0].Scopes.Single(scope => scope.Name == "Visible Variables");
        Assert.Contains(visible.Variables, variable => variable.Name == "x" && variable.Value == "1");
        await session.ContinueAsync(timeout.Token);
        await terminated.Task.WaitAsync(timeout.Token);
        Assert.Contains("1" + Environment.NewLine, output);
    }

    [Fact]
    public async Task AttachedSessionRetrievesSourceStopsOnEntryAndDetachesWithoutTerminatingHost()
    {
        const string sourceCode = "let x = 1;\nx = x + 1;\nprintln(x);";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = sourceCode,
            SourceName = "remote.fb",
            SourceIdentity = "fishbone://remote/remote.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        }, timeout.Token);
        await using FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var pauses = Channel.CreateUnbounded<FishbonePauseSnapshot>();
        client.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused paused)
                pauses.Writer.TryWrite(paused.Snapshot);
        };

        FishboneDebugSource source = await client.ConnectAsync(stopOnEntry: true, timeout.Token);
        Assert.Equal(sourceCode, source.Content);
        Assert.Equal("fishbone://remote/remote.fb", source.Identity);
        await client.ConfigureAsync([], timeout.Token);

        FishbonePauseSnapshot entry = await pauses.Reader.ReadAsync(timeout.Token);
        Assert.Equal("entry", entry.Reason, ignoreCase: true);
        await client.SetBreakpointsAsync([2], timeout.Token);
        await client.ContinueAsync(timeout.Token);
        FishbonePauseSnapshot breakpoint = await pauses.Reader.ReadAsync(timeout.Token);
        FishboneDebugScope visible = breakpoint.Frames[0].Scopes.Single(scope => scope.Name == "Visible Variables");
        Assert.Contains(visible.Variables, variable => variable.Name == "x" && variable.Value == "1");

        await client.DisconnectAsync(timeout.Token);
        FishboneDebugServerResult result = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.WasCancelled);
        Assert.NotNull(result.Environment);
        Assert.Equal(2d, Convert.ToDouble(result.Environment!.GetValue("x")));
    }

    [Fact]
    public async Task HostCancellationUnblocksAnEntryPausedExecution()
    {
        using var hostCancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = "let value = 1;",
            SourceName = "cancel.fb",
            SourceIdentity = "fishbone://remote/cancel.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        }, hostCancellation.Token);
        await using FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused)
                paused.TrySetResult();
        };

        await client.ConnectAsync(stopOnEntry: true, timeout.Token);
        await client.ConfigureAsync([], timeout.Token);
        await paused.Task.WaitAsync(timeout.Token);
        await hostCancellation.CancelAsync();

        FishboneDebugServerResult result = await server.Completion.WaitAsync(timeout.Token);
        Assert.True(result.WasCancelled);
        Assert.Equal(1, result.ExitCode);
    }
}