using Fishbone.DebugClient;
using Fishbone.DebugAdapter;
using Fishbone.Debugging;
using System.Diagnostics;
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

        // continuing to the end ends the session, without a pause there
        await terminated.Task.WaitAsync(timeout.Token);
        Assert.False(paused.Reader.TryRead(out _));
        Assert.Contains("1" + Environment.NewLine, output);
    }

    [Fact]
    public async Task AttachedSession_ContinuingToTheEnd_EndsWithoutAPause()
    {
        await using var attached = await AttachedAtLine2Async();

        await attached.Client.ContinueAsync(attached.Timeout.Token);

        FishboneDebugServerResult result = await attached.Server.Completion.WaitAsync(attached.Timeout.Token);
        await attached.Terminated.Task.WaitAsync(attached.Timeout.Token);
        Assert.Equal(2, result.Environment!.GetValue("x"));
        Assert.False(attached.Pauses.Reader.TryRead(out _));
    }

    [Fact]
    public async Task AttachedSession_SteppingOffTheEnd_StopsThereWithTheFinalValues()
    {
        await using var attached = await AttachedAtLine2Async();

        await attached.Client.StepOverAsync(attached.Timeout.Token);
        FishbonePauseSnapshot end = await attached.Pauses.Reader.ReadAsync(attached.Timeout.Token);

        Assert.Equal(FishbonePauseSnapshot.ProgramExitReason, end.Reason);
        FishboneDebugScope final = end.Frames[0].Scopes.Single(scope => scope.Name == "Locals");
        Assert.Contains(final.Variables, variable => variable.Name == "x" && variable.Value == "2");
        await attached.Client.ContinueAsync(attached.Timeout.Token);
        await attached.Server.Completion.WaitAsync(attached.Timeout.Token);
        await attached.Terminated.Task.WaitAsync(attached.Timeout.Token);
    }

    private sealed record Attached(FishboneDebugServerSession Server, FishboneDebugClientSession Client,
        Channel<FishbonePauseSnapshot> Pauses, TaskCompletionSource Terminated, CancellationTokenSource Timeout) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            Timeout.Dispose();
        }
    }

    // attached like SpineIDE does, and paused at the breakpoint on the last line
    private static async Task<Attached> AttachedAtLine2Async()
    {
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = "let x = 1;\nx = x + 1;",
            SourceName = "remote.fb",
            SourceIdentity = "fishbone://remote/end.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        }, timeout.Token);
        FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var pauses = Channel.CreateUnbounded<FishbonePauseSnapshot>();
        var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused paused)
                pauses.Writer.TryWrite(paused.Snapshot);
            if (debugEvent is FishboneDebugTerminated)
                terminated.TrySetResult();
        };
        await client.ConnectAsync(stopOnEntry: false, timeout.Token);
        await client.ConfigureAsync([2], timeout.Token);
        FishbonePauseSnapshot breakpoint = await pauses.Reader.ReadAsync(timeout.Token);
        Assert.Equal(2, breakpoint.Frames[0].Line);
        return new Attached(server, client, pauses, terminated, timeout);
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
    public async Task ImageVariableIsFetchedWithItsShapesWhilePaused()
    {
        const string sourceCode = """
let x = 1;
x = 2;
""";
        var configuration = new FishboneConfiguration()
            .AddValue("picture", new Picture(200))
            .AddVisualizer<Picture>(picture => new FishboneImage(2, 1, 1, [picture.Level, 0])
            {
                Regions = [new FishboneRegion([0], [0], [1])],
                Contours = [new FishboneContour([0.5, 0.25], [0, 1.5])],
            });
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
        FishboneDebugImage image = await client.GetImageAsync(picture.ImageHandle, timeout.Token);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, image.Png[..8]);
        Assert.Equal((2, 1), (image.Width, image.Height));
        FishboneDebugRegion region = Assert.Single(image.Regions);
        Assert.Equal([0], region.Rows);
        Assert.Equal([0], region.ColumnStarts);
        Assert.Equal([1], region.ColumnEnds);
        FishboneDebugContour contour = Assert.Single(image.Contours);
        Assert.Equal([0.5, 0.25], contour.Rows);
        Assert.Equal([0, 1.5], contour.Columns);

        // the handle belongs to this pause
        await client.ContinueAsync(timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetImageAsync(picture.ImageHandle, timeout.Token));

        FishboneDebugServerResult result = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task ImageHoldingSeveral_IsAnImageWithImageChildren()
    {
        var configuration = new FishboneConfiguration()
            .AddValue("stack", new Picture[] { new(10), new(20) })
            .AddVisualizer<Picture>(picture => new FishboneImage(1, 1, 1, [picture.Level]))
            .AddVisualizer<Picture[]>(pictures => new FishboneImage(1, 1, 1, [pictures[0].Level]),
                children: pictures => pictures.Select((picture, index) => ($"[{index + 1}]", (object?)picture)).ToArray());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = "let x = 1;\nx = 2;",
            SourceName = "stack.fb",
            SourceIdentity = "fishbone://tests/stack.fb",
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
        FishboneDebugVariable stack = pause.Frames[0].Scopes.Single(scope => scope.Name == "Locals").Variables.Single(variable => variable.Name == "stack");

        Assert.NotNull(stack.ImageHandle);
        Assert.NotNull(stack.ChildrenHandle);
        IReadOnlyList<FishboneDebugVariable> children = await client.GetVariablesAsync(stack.ChildrenHandle, timeout.Token);
        Assert.Equal(["[1]", "[2]"], children.Select(child => child.Name));
        Assert.All(children, child => Assert.Null(child.ChildrenHandle));
        FishboneDebugImage second = await client.GetImageAsync(children[1].ImageHandle!, timeout.Token);
        Assert.Equal((1, 1), (second.Width, second.Height));

        await client.ContinueAsync(timeout.Token);
        await server.Completion.WaitAsync(timeout.Token);
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

    [Fact]
    public async Task EachStepsPause_CanExpandItsVariables()
    {
        // the adapter can announce the next pause before it answers the step request. the client
        // used to retire the pause handles when that answer arrived, killing the new pause's handles
        const int steps = 40;
        string sourceCode = "let items = [1, 2, 3];\n" + string.Concat(Enumerable.Repeat("items.Add(4);\n", steps));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = sourceCode,
            SourceName = "steps.fb",
            SourceIdentity = "fishbone://tests/steps.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        }, timeout.Token);
        await using FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var pauses = Channel.CreateUnbounded<FishbonePauseSnapshot>();
        client.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused paused)
                pauses.Writer.TryWrite(paused.Snapshot);
        };
        await client.ConnectAsync(stopOnEntry: true, timeout.Token);
        await client.ConfigureAsync([], timeout.Token);
        await pauses.Reader.ReadAsync(timeout.Token);

        for (int step = 0; step < steps; step++)
        {
            await client.StepOverAsync(timeout.Token);
            FishbonePauseSnapshot pause = await pauses.Reader.ReadAsync(timeout.Token);
            var items = pause.Frames[0].Scopes.Single(scope => scope.Name == "Locals").Variables.Single(variable => variable.Name == "items");

            var children = await client.GetVariablesAsync(items.ChildrenHandle!, timeout.Token);

            Assert.Equal(3 + step, children.Count);
        }

        await client.DisconnectAsync(timeout.Token);
    }

    [Fact]
    public async Task LaunchedSessionPausesOnARuntimeErrorThenFailsWithExitCodeOne()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"fishbone-client-{Guid.NewGuid():N}.fb");
        await File.WriteAllTextAsync(scriptPath, "let x = 1;\nlet y = null;\ny.Anything();\nx = 2;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = new FishboneDebugClientSession(scriptPath, new FishboneDapHostLocator(AppContext.BaseDirectory));
        var events = Channel.CreateUnbounded<FishboneDebugEvent>();
        session.EventReceived += (_, debugEvent) => events.Writer.TryWrite(debugEvent);

        await session.StartAsync([], timeout.Token);
        FishbonePauseSnapshot pause = (await NextAsync<FishboneDebugPaused>(events.Reader, timeout.Token)).Snapshot;
        Assert.Equal("exception", pause.Reason, ignoreCase: true);
        Assert.Equal(3, pause.Frames[0].Line);
        Assert.NotNull(pause.Exception);
        Assert.False(string.IsNullOrWhiteSpace(pause.Exception!.Description));
        await session.ContinueAsync(timeout.Token);

        var after = new List<FishboneDebugEvent>();
        FishboneDebugEvent next;
        do
        {
            next = await events.Reader.ReadAsync(timeout.Token);
            after.Add(next);
            // a launched host pauses at the end to show final values, but a failed run has none,
            // so continuing once ends it. a second pause here used to need another continue
            if (next is FishboneDebugPaused)
                break;
        } while (next is not FishboneDebugTerminated);

        Assert.DoesNotContain(after, debugEvent => debugEvent is FishboneDebugPaused);
        Assert.Single(after.OfType<FishboneDebugFailed>());
        Assert.Equal(1, ((FishboneDebugTerminated)next).ExitCode);
        Assert.Equal(FishboneDebugSessionState.Completed, session.State);
    }

    [Fact]
    public async Task StoppingALaunchedSessionWhilePausedEndsTheHost()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"fishbone-client-{Guid.NewGuid():N}.fb");
        await File.WriteAllTextAsync(scriptPath, "let x = 1;\nx = 2;\nx = 3;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = new FishboneDebugClientSession(scriptPath, new FishboneDapHostLocator(AppContext.BaseDirectory));
        var events = Channel.CreateUnbounded<FishboneDebugEvent>();
        session.EventReceived += (_, debugEvent) => events.Writer.TryWrite(debugEvent);
        HashSet<int> existing = HostProcessIds();

        await session.StartAsync([2], timeout.Token);
        using Process host = LaunchedHost(existing);
        await NextAsync<FishboneDebugPaused>(events.Reader, timeout.Token);
        await session.StopAsync(timeout.Token);

        await NextAsync<FishboneDebugTerminated>(events.Reader, timeout.Token);
        await host.WaitForExitAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(5));
        // the host ended the cancelled run itself. a kill would leave -1
        Assert.Equal(1, host.ExitCode);
        Assert.Equal(FishboneDebugSessionState.Completed, session.State);
    }

    [Fact]
    public async Task HostCrashingWhilePausedFaultsTheSession()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"fishbone-client-{Guid.NewGuid():N}.fb");
        await File.WriteAllTextAsync(scriptPath, "let x = 1;\nx = 2;\nx = 3;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = new FishboneDebugClientSession(scriptPath, new FishboneDapHostLocator(AppContext.BaseDirectory));
        var events = Channel.CreateUnbounded<FishboneDebugEvent>();
        session.EventReceived += (_, debugEvent) => events.Writer.TryWrite(debugEvent);
        HashSet<int> existing = HostProcessIds();

        await session.StartAsync([2], timeout.Token);
        using Process host = LaunchedHost(existing);
        await NextAsync<FishboneDebugPaused>(events.Reader, timeout.Token);
        host.Kill();

        await NextAsync<FishboneDebugFailed>(events.Reader, timeout.Token);
        Assert.Equal(FishboneDebugSessionState.Faulted, session.State);
    }

    [Fact]
    public async Task PauseStopsARunningScriptAndContinueResumesIt()
    {
        const string sourceCode = """
ready();
let n = 0;
while (keepGoing()) {
    n = n + 1;
}
let done = true;
""";
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var finish = new ManualResetEventSlim();
        var configuration = new FishboneConfiguration()
            .AddBuiltIn("ready", new Action(() => ready.TrySetResult()))
            .AddBuiltIn("keepGoing", new Func<bool>(() => !finish.IsSet));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = sourceCode,
            SourceName = "loop.fb",
            SourceIdentity = "fishbone://tests/loop.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0),
            Configuration = configuration
        }, timeout.Token);
        await using FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var events = Channel.CreateUnbounded<FishboneDebugEvent>();
        client.EventReceived += (_, debugEvent) => events.Writer.TryWrite(debugEvent);

        await client.ConnectAsync(stopOnEntry: false, timeout.Token);
        await client.ConfigureAsync([], timeout.Token);
        await ready.Task.WaitAsync(timeout.Token);
        await client.PauseAsync(timeout.Token);

        FishbonePauseSnapshot pause = (await NextAsync<FishboneDebugPaused>(events.Reader, timeout.Token)).Snapshot;
        Assert.Equal("pause", pause.Reason, ignoreCase: true);
        Assert.InRange(pause.Frames[0].Line, 3, 4);
        await client.ContinueAsync(timeout.Token);
        await NextAsync<FishboneDebugContinued>(events.Reader, timeout.Token);
        finish.Set();

        FishboneDebugServerResult result = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(true, result.Environment!.GetValue("done"));
    }

    [Fact]
    public async Task FunctionFrameShowsItsLocalsTheVisibleVariablesAndTheGlobals()
    {
        const string sourceCode = """
let total = 100;
func add(a, b) {
    let sum = a + b;
    return sum;
}
let result = add(1, 2);
""";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = sourceCode,
            SourceName = "scopes.fb",
            SourceIdentity = "fishbone://tests/scopes.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        }, timeout.Token);
        await using FishboneDebugClientSession client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        var events = Channel.CreateUnbounded<FishboneDebugEvent>();
        client.EventReceived += (_, debugEvent) => events.Writer.TryWrite(debugEvent);

        await client.ConnectAsync(stopOnEntry: false, timeout.Token);
        await client.ConfigureAsync([4], timeout.Token);
        FishbonePauseSnapshot pause = (await NextAsync<FishboneDebugPaused>(events.Reader, timeout.Token)).Snapshot;

        Assert.Equal(["add", "<script>"], pause.Frames.Select(frame => frame.Name));
        FishboneDebugFrame function = pause.Frames[0];
        Assert.Equal(["Locals", "Visible Variables", "Globals"], function.Scopes.Select(scope => scope.Name));
        // the body's lets are locals too, though the body is a block of its own
        Assert.Equal(["a=1", "b=2", "sum=3"], Names(function.Scopes[0]));
        Assert.Equal(["a=1", "add=func add(a, b)", "b=2", "sum=3", "total=100"], Names(function.Scopes[1]));
        Assert.Equal(["add=func add(a, b)", "total=100"], Names(function.Scopes[2]));

        // the outermost frame is the globals, so it only has its own locals
        FishboneDebugScope script = Assert.Single(pause.Frames[1].Scopes);
        Assert.Equal("Locals", script.Name);
        Assert.Equal(6, pause.Frames[1].Line);

        await client.ContinueAsync(timeout.Token);
        FishboneDebugServerResult result = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(0, result.ExitCode);
    }

    private static IEnumerable<string> Names(FishboneDebugScope scope) =>
        scope.Variables.Select(variable => $"{variable.Name}={variable.Value}").Order(StringComparer.Ordinal);

    private static async Task<T> NextAsync<T>(ChannelReader<FishboneDebugEvent> events, CancellationToken cancellationToken)
        where T : FishboneDebugEvent
    {
        while (true)
            if (await events.ReadAsync(cancellationToken) is T match)
                return match;
    }

    private static readonly string HostPath = new FishboneDapHostLocator(AppContext.BaseDirectory).Locate().FileName;

    private static HashSet<int> HostProcessIds() =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(HostPath)).Select(process => process.Id).ToHashSet();

    // a launched session keeps its host process private. the new process running this
    // test's own copy of fishbone-dap is the one it started
    private static Process LaunchedHost(HashSet<int> existing)
    {
        Process host = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(HostPath)).Single(process =>
            !existing.Contains(process.Id) &&
            string.Equals(process.MainModule?.FileName, HostPath, StringComparison.OrdinalIgnoreCase));
        // opening the handle now keeps the exit code readable after the process ends
        _ = host.Handle;
        return host;
    }
}