using System.Net;
using Fishbone.Engine;

namespace Fishbone.DebugAdapter.Tests;

public class RunDebuggableAsyncTests
{
    [Fact]
    public async Task RunDebuggableAsync_NoClientAttaches_FallsBackToHeadless()
    {
        var program = FishboneProgram.ParseSource("let result = 21 * 2;");

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
        var program = FishboneProgram.ParseSource("let x = 7;");
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
        var program = FishboneProgram.ParseSource("let doubled = seed * 2;");
        var config = new FishboneConfiguration().AddValue("seed", 50);

        var result = await program.RunDebuggableAsync(config, new FishboneDebugOptions
        {
            OpenIde = false,
            AttachTimeout = TimeSpan.FromMilliseconds(200),
        });

        Assert.Equal(100, result.Environment!.GetValue("doubled"));
    }
}