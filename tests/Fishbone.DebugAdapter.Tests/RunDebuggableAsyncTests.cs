using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task? client = null;

        var result = await program.RunDebuggableAsync(new FishboneConfiguration(), new FishboneDebugOptions
        {
            OpenIde       = true,
            AttachTimeout = TimeSpan.FromSeconds(30),
            IdeLauncher   = endpoint =>
            {
                client = Task.Run(async () =>
                {
                    await using var session = FishboneDebugClientSession.Attach("127.0.0.1", endpoint.Port);
                    var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    session.EventReceived += (_, e) =>
                    {
                        if (e is FishboneDebugPaused)
                            paused.TrySetResult();
                        if (e is FishboneDebugTerminated)
                            terminated.TrySetResult();
                    };

                    await session.ConnectAsync(stopOnEntry: true, timeout.Token);
                    await session.ConfigureAsync([], timeout.Token);
                    await paused.Task.WaitAsync(timeout.Token);
                    await session.ContinueAsync(timeout.Token);
                    await terminated.Task.WaitAsync(timeout.Token);
                });
                return null;   // no process to launch, we attached in-proc
            },
        });

        await client!.WaitAsync(timeout.Token);
        Assert.True(result.DebuggerAttached);
        Assert.False(result.WasCancelled);
        Assert.Null(result.Error);
        Assert.NotNull(result.Environment);
        Assert.Equal(42, result.Environment!.GetValue("doubled"));
    }

    [Fact]
    public async Task RunDebuggableAsync_ClientVanishesWhilePaused_ScriptStillRunsToCompletion()
    {
        // closing the debugger window kills the socket without sending a disconnect request.
        // the script should carry on as if continued, not be cancelled half way
        var program = FishboneProgram.FromSourceCode("let seed = 21; let doubled = seed * 2;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task? client = null;

        var result = await program.RunDebuggableAsync(new FishboneConfiguration(), new FishboneDebugOptions
        {
            OpenIde       = true,
            AttachTimeout = TimeSpan.FromSeconds(30),
            IdeLauncher   = endpoint =>
            {
                client = Task.Run(async () =>
                {
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync(IPAddress.Loopback, endpoint.Port, timeout.Token);
                    var dap = new TcpDapIntegrationTests.RawDapClient(tcp.GetStream(), timeout.Token);

                    await dap.RequestAsync("initialize", new { adapterID = "fishbone" });
                    await dap.RequestAsync("attach", new { stopOnEntry = true });
                    await dap.RequestAsync("configurationDone", new { });
                    JsonElement stopped = await dap.ReadUntilAsync(message =>
                        message.TryGetProperty("event", out var name) && name.GetString() == "stopped");
                    Assert.Equal("entry", stopped.GetProperty("body").GetProperty("reason").GetString());

                    tcp.Client.Close(0);   // abortive, no FIN and no disconnect request
                });
                return null;
            },
        });

        await client!.WaitAsync(timeout.Token);
        Assert.True(result.DebuggerAttached);
        Assert.False(result.WasCancelled);
        Assert.NotNull(result.Environment);
        Assert.Equal(42, result.Environment!.GetValue("doubled"));
    }
}