using System.Net;
using Fishbone;
using Fishbone.DebugClient;

namespace Fishbone.DebugAdapter.Tests;

[Collection("DebugServer")]
public class RunDebuggableAsyncTests
{
    [Fact]
    public async Task RunDebuggableAsync_NoClientAttaches_FallsBackToHeadless()
    {
        var program = FishboneProgram.FromSourceCode("let result = 21 * 2;");

        var result = await program.RunDebuggableAsync(new FishboneConfiguration(), new FishboneDebugOptions
        {
            OpenIde = false,
            AttachTimeout = TimeSpan.FromMilliseconds(200),
        });

        Assert.False(result.DebuggerAttached);
        Assert.Null(result.Error);
        Assert.NotNull(result.Environment);
        Assert.Equal(42, result.Environment!.GetValue("result"));
    }

    [Fact]
    public async Task RunDebuggableAsync_InvokesIdeLauncherWithEndpoint_ThenFallsBackWhenNobodyAttaches()
    {
        var program = FishboneProgram.FromSourceCode("let x = 7;");
        IPEndPoint? launchedEndpoint = null;

        var result = await program.RunDebuggableAsync(new FishboneConfiguration(), new FishboneDebugOptions
        {
            OpenIde = true,
            AttachTimeout = TimeSpan.FromMilliseconds(200),
            // Simulate an IDE that is launched but never attaches.
            IdeLauncher = endpoint => { launchedEndpoint = endpoint; return null; },
        });

        Assert.NotNull(launchedEndpoint);
        Assert.True(launchedEndpoint!.Port > 0);
        Assert.False(result.DebuggerAttached);
        Assert.Equal(7, result.Environment!.GetValue("x"));
    }

    [Fact]
    public async Task RunDebuggableAsync_HeadlessFallback_SeesInjectedConfiguration()
    {
        var program = FishboneProgram.FromSourceCode("let doubled = seed * 2;");
        var config = new FishboneConfiguration().AddValue("seed", 50);

        var result = await program.RunDebuggableAsync(config, new FishboneDebugOptions
        {
            OpenIde = false,
            AttachTimeout = TimeSpan.FromMilliseconds(200),
        });

        Assert.Equal(100, result.Environment!.GetValue("doubled"));
    }

    [Fact]
    public async Task RunDebuggableAsync_ClientAttachesAndContinues_ReturnsTheEnvironment()
    {
        // the fallback paths above never exercise a real client. this drives an actual DAP
        // attach through IdeLauncher, which hands us the endpoint, and continues to the end
        var program = FishboneProgram.FromSourceCode("let seed = 21; let doubled = seed * 2;");

        var result = await program.RunDebuggableAsync(new FishboneConfiguration(), new FishboneDebugOptions
        {
            OpenIde       = true,
            AttachTimeout = TimeSpan.FromSeconds(30),
            IdeLauncher   = endpoint =>
            {
                _ = Task.Run(async () =>
                {
                    await using var session = FishboneDebugClientSession.Attach("127.0.0.1", endpoint.Port);
                    await session.ConnectAsync(stopOnEntry: true);
                    await session.ConfigureAsync([]);

                    var terminated = new TaskCompletionSource();
                    session.EventReceived += (_, e) =>
                    {
                        if (e is FishboneDebugTerminated)
                            terminated.TrySetResult();
                    };

                    await session.ContinueAsync();
                    await terminated.Task.WaitAsync(TimeSpan.FromSeconds(20));
                });
                return null;   // no process to launch, we attached in-proc
            },
        });

        Assert.True(result.DebuggerAttached);
        Assert.False(result.WasCancelled);
        Assert.Null(result.Error);
        Assert.NotNull(result.Environment);
        Assert.Equal(42, result.Environment!.GetValue("doubled"));
    }
}