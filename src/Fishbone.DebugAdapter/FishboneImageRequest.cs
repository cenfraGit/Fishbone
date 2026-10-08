using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace Fishbone.DebugAdapter;

/// <summary>
/// A custom DAP request, <c>fishbone/image</c>: the image behind a variable marked with
/// <see cref="DebugSnapshotHandles.ImageKind"/>, encoded as PNG. Only valid while paused.
/// </summary>
[Method(FishboneImageArguments.Command, Direction.ClientToServer)]
public sealed record FishboneImageArguments : IRequest<FishboneImageResponse>
{
    public const string Command = "fishbone/image";

    public long VariablesReference { get; init; }
}

/// <summary>The PNG bytes, base64 encoded, since DAP messages are JSON.</summary>
public sealed record FishboneImageResponse
{
    public string Png { get; init; } = string.Empty;
}
