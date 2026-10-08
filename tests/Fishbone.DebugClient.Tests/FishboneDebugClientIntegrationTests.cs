using Fishbone.DebugClient;
using Fishbone.DebugAdapter;
using Fishbone.Debugging;
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
        var paused = Channel.CreateUnbounded<FishbonePauseSnapshot>();
        var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new List<string>();
        session.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused value) paused.Writer.TryWrite(value.Snapshot);
            if (debugEvent is FishboneDebugTerminated) terminated.TrySetResult();
            if (debugEvent is FishboneDebugOutput valueOutput) output.Add(valueOutput.Text);
        };

        await session.StartAsync([2], timeout.Token);
        FishbonePauseSnapshot snapshot = await paused.Reader.ReadAsync(timeout.Token);

        FishboneDebugScope visible = snapshot.Frames[0].Scopes.Single(scope => scope.Name == "Locals"); // at global scope "Visible Variables" dedupes into "Locals"
        Assert.Contains(visible.Variables, variable => variable.Name == "x" && variable.Value == "1");
        await session.ContinueAsync(timeout.Token);

        // a session that launched its own host pauses once more at the end, with the final values
        FishbonePauseSnapshot end = await paused.Reader.ReadAsync(timeout.Token);
        Assert.Equal(FishbonePauseSnapshot.ProgramExitReason, end.Reason);
        FishboneDebugScope final = end.Frames[0].Scopes.Single(scope => scope.Name == "Locals");
        Assert.Contains(final.Variables, variable => variable.Name == "x" && variable.Value == "2");
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
        FishboneDebugScope visible = breakpoint.Frames[0].Scopes.Single(scope => scope.Name == "Locals"); // at global scope "Visible Variables" dedupes into "Locals"
        Assert.Contains(visible.Variables, variable => variable.Name == "x" && variable.Value == "1");

        await client.DisconnectAsync(timeout.Token);
        FishboneDebugServerResult result = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.WasCancelled);
        Assert.NotNull(result.Environment);
        Assert.Equal(2d, Convert.ToDouble(result.Environment!.GetValue("x")));
    }

    [Fact]
    public async Task BreakpointOnBraceLineHitsTheNextStatementAndStaysOnItsLine()
    {
        const string sourceCode = """
let c = [1, 2];
foreach (i in c)
{
    println(i);
}
""";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = sourceCode,
            SourceName = "brace.fb",
            SourceIdentity = "fishbone://tests/brace.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        }, timeout.Token);
        await using FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var pauses = Channel.CreateUnbounded<FishbonePauseSnapshot>();
        client.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused paused)
                pauses.Writer.TryWrite(paused.Snapshot);
        };

        await client.ConnectAsync(stopOnEntry: false, timeout.Token);
        var results = await client.ConfigureAsync([3], timeout.Token);

        // the result keeps the line the user clicked, so the editor's dot stays on the brace
        var result = Assert.Single(results);
        Assert.Equal(3, result.Line);
        Assert.True(result.Verified);

        // it hits on the println line, once per iteration
        Assert.Equal(4, (await pauses.Reader.ReadAsync(timeout.Token)).Frames[0].Line);
        await client.ContinueAsync(timeout.Token);
        Assert.Equal(4, (await pauses.Reader.ReadAsync(timeout.Token)).Frames[0].Line);
        await client.ContinueAsync(timeout.Token);

        FishboneDebugServerResult final = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(0, final.ExitCode);
    }

    [Fact]
    public async Task ImageVariableIsFetchedAsPngWhilePaused()
    {
        const string sourceCode = """
let x = 1;
x = 2;
""";
        var configuration = new FishboneConfiguration()
            .AddValue("picture", new Picture(200))
            .AddVisualizer<Picture>(picture => new FishboneImage(2, 1, 1, [picture.Level, 0]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = sourceCode,
            SourceName = "image.fb",
            SourceIdentity = "fishbone://tests/image.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0),
            Configuration = configuration
        }, timeout.Token);
        await using FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var pauses = Channel.CreateUnbounded<FishbonePauseSnapshot>();
        client.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused paused)
                pauses.Writer.TryWrite(paused.Snapshot);
        };

        await client.ConnectAsync(stopOnEntry: false, timeout.Token);
        await client.ConfigureAsync([2], timeout.Token);
        FishbonePauseSnapshot pause = await pauses.Reader.ReadAsync(timeout.Token);
        var locals = pause.Frames[0].Scopes.Single(scope => scope.Name == "Locals").Variables;
        FishboneDebugVariable picture = locals.Single(variable => variable.Name == "picture");
        FishboneDebugVariable x = locals.Single(variable => variable.Name == "x");

        Assert.Null(x.ImageHandle);
        Assert.NotNull(picture.ImageHandle);
        byte[] png = await client.GetImageAsync(picture.ImageHandle, timeout.Token);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);

        // the handle belongs to this pause
        await client.ContinueAsync(timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetImageAsync(picture.ImageHandle, timeout.Token));

        FishboneDebugServerResult result = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(0, result.ExitCode);
    }

    private sealed class Picture(byte level)
    {
        public byte Level { get; } = level;
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