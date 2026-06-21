using System.Net;
using Fishbone.DebugAdapter;

namespace Fishbone.Dap;

public sealed class FishboneDapHost
{
    public async Task<int> RunAsync(string scriptPath, int port, TextWriter endpointWriter, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(scriptPath);
        string source = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        await using FishboneDebugServerSession server = await FishboneDebugServer.StartAsync(new FishboneDebugServerOptions
        {
            SourceCode = source,
            SourceName = Path.GetFileName(fullPath),
            SourceIdentity = fullPath,
            ListenEndpoint = new IPEndPoint(IPAddress.Loopback, port)
        }, cancellationToken).ConfigureAwait(false);

        await endpointWriter.WriteLineAsync(server.Endpoint.ToString()).ConfigureAwait(false);
        await endpointWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
        FishboneDebugServerResult result = await server.Completion.ConfigureAwait(false);
        return result.ExitCode;
    }
}