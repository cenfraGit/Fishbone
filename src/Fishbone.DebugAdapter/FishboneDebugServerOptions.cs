using System.Net;
using Fishbone.Engine;

namespace Fishbone.DebugAdapter;

public sealed class FishboneDebugServerOptions
{
    public required string SourceCode { get; init; }
    public required string SourceName { get; init; }
    public required string SourceIdentity { get; init; }
    public FishboneConfiguration Configuration { get; init; } = new();
    public IPEndPoint ListenEndpoint { get; init; } = new(IPAddress.Loopback, 0);
    public bool RedirectOutput { get; init; } = true;
}