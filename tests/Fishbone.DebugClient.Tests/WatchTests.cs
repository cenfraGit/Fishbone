using Fishbone.DebugAdapter;
using System.Net;
using System.Threading.Channels;

namespace Fishbone.DebugClient.Tests;

/// <summary>Watches, evaluated by the debug adapter in the paused script's scope.</summary>
public class WatchTests
{
    private const string Script = """
        func scale(x)
        {
            return x * 10;
        }
        let items = [1, 2, 3];
        let total = 6;
        let scaled = scale(total);
        println(scaled);
        """;

    [Fact]
    public async Task Watches_AreEvaluatedInThePausedFrame()
    {
        await using var paused = await PausedSession.StartAsync([3, 8]);

        // inside scale, where x is the argument
        Assert.Equal("7", (await paused.Client.EvaluateAsync("x + 1", paused.Timeout)).Value);

        await paused.ContinueToNextPauseAsync();
        Assert.Equal("12", (await paused.Client.EvaluateAsync("total * 2", paused.Timeout)).Value);
        // a call runs, in the script's own scope
        Assert.Equal("60", (await paused.Client.EvaluateAsync("scale(total)", paused.Timeout)).Value);

        FishboneDebugVariable items = await paused.Client.EvaluateAsync("items", paused.Timeout);
        Assert.Equal("items", items.Name);
        Assert.Equal(3, (await paused.Client.GetVariablesAsync(items.ChildrenHandle!, paused.Timeout)).Count);
    }

    // like count_obj: a host built-in that answers through an out argument
    private delegate void CountDelegate(List<object?> items, out int count);

    [Fact]
    public async Task AWatchOnABuiltInWithAnOut_ShowsTheOutValue()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("count_items", new CountDelegate((List<object?> items, out int count) => count = items.Count));
        await using var paused = await PausedSession.StartAsync([8], config);

        Assert.Equal("3", (await paused.Client.EvaluateAsync("count_items(items, out n)", paused.Timeout)).Value);
    }

    [Fact]
    public async Task AWatchThatFails_SaysWhy()
    {
        await using var paused = await PausedSession.StartAsync([8]);

        var undefined = await Assert.ThrowsAnyAsync<Exception>(() => paused.Client.EvaluateAsync("nope + 1", paused.Timeout));
        var unfinished = await Assert.ThrowsAnyAsync<Exception>(() => paused.Client.EvaluateAsync("total +", paused.Timeout));

        Assert.Contains("nope", undefined.Message);
        Assert.NotEmpty(unfinished.Message);
        // the session is still paused and usable
        Assert.Equal("6", (await paused.Client.EvaluateAsync("total", paused.Timeout)).Value);
    }

    [Fact]
    public async Task Completions_OfferTheNamesInScope_AndTheMembersOfValues()
    {
        await using var paused = await PausedSession.StartAsync([3, 8]);

        // x is a parameter, so only its value says it's an int
        FishboneDebugCompletions? members = await paused.Client.GetCompletionsAsync("x.Comp", 6, paused.Timeout);
        Assert.NotNull(members);
        Assert.Equal(2, members.Start);
        Assert.Contains("CompareTo", members.Items);

        await paused.ContinueToNextPauseAsync();
        FishboneDebugCompletions? names = await paused.Client.GetCompletionsAsync("1 + to", 6, paused.Timeout);
        Assert.NotNull(names);
        Assert.Equal(4, names.Start);
        Assert.Contains("total", names.Items);

        FishboneDebugCompletions? listMembers = await paused.Client.GetCompletionsAsync("items.", 6, paused.Timeout);
        Assert.NotNull(listMembers);
        Assert.Contains("Count", listMembers.Items);
    }

    [Fact]
    public async Task AClientThatGoesAwayWhilePaused_LetsAHostHeldAtTheEndFinish()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = Script,
            SourceName = "watch.fb",
            SourceIdentity = "fishbone://tests/held.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        }, timeout.Token);
        var client = FishboneDebugClientSession.Attach("127.0.0.1", server.Endpoint.Port);
        client.PauseAtEnd = true;
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused)
                paused.TrySetResult();
        };
        await client.ConnectAsync(stopOnEntry: false, timeout.Token);
        await client.ConfigureAsync([8], timeout.Token);
        await paused.Task.WaitAsync(timeout.Token);

        // like closing SpineIDE: the script runs on, and doesn't stop at its end for no one
        await client.DisposeAsync();

        var result = await server.Completion.WaitAsync(timeout.Token);
        Assert.Equal(60, result.Environment!.GetValue("scaled"));
    }

    // a session attached to the script above and paused at its first breakpoint
    private sealed class PausedSession : IAsyncDisposable
    {
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private readonly Channel<FishbonePauseSnapshot> _pauses = Channel.CreateUnbounded<FishbonePauseSnapshot>();
        private FishboneDebugServerSession _server = null!;

        public FishboneDebugClientSession Client { get; private set; } = null!;
        public CancellationToken Timeout => _timeout.Token;

        public static async Task<PausedSession> StartAsync(int[] breakpoints, FishboneConfiguration? configuration = null)
        {
            var paused = new PausedSession();
            paused._server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
            {
                SourceCode = Script,
                SourceName = "watch.fb",
                SourceIdentity = "fishbone://tests/watch.fb",
                Configuration = configuration ?? new FishboneConfiguration(),
                ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
            }, paused.Timeout);
            paused.Client = FishboneDebugClientSession.Attach("127.0.0.1", paused._server.Endpoint.Port);
            paused.Client.EventReceived += (_, debugEvent) =>
            {
                if (debugEvent is FishboneDebugPaused pause)
                    paused._pauses.Writer.TryWrite(pause.Snapshot);
            };
            await paused.Client.ConnectAsync(stopOnEntry: false, paused.Timeout);
            await paused.Client.ConfigureAsync(breakpoints, paused.Timeout);
            await paused._pauses.Reader.ReadAsync(paused.Timeout);
            return paused;
        }

        public async Task ContinueToNextPauseAsync()
        {
            await Client.ContinueAsync(Timeout);
            await _pauses.Reader.ReadAsync(Timeout);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisconnectAsync(Timeout);
            await Client.DisposeAsync();
            await _server.DisposeAsync();
            _timeout.Dispose();
        }
    }
}
