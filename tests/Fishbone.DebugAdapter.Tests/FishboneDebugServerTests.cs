using System.Net;
using Fishbone.DebugAdapter;

namespace Fishbone.DebugAdapter.Tests;

public class FishboneDebugServerTests
{
    [Fact]
    public async Task StartBindsEndpointWithoutStartingExecution()
    {
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = "let value = 1;",
            SourceName = "embedded.fb",
            SourceIdentity = "fishbone://tests/embedded.fb",
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0)
        });

        Assert.Equal(IPAddress.Loopback, server.Endpoint.Address);
        Assert.InRange(server.Endpoint.Port, 1, 65535);
        Assert.False(server.Completion.IsCompleted);

        await server.StopAsync();
        FishboneDebugServerResult result = await server.Completion;
        Assert.True(result.WasCancelled);
    }
}