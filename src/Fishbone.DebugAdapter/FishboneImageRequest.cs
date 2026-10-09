using Fishbone.Debugging;
using MediatR;
using OmniSharp.Extensions.JsonRpc;

namespace Fishbone.DebugAdapter;

/// <summary>
/// A custom DAP request, <c>fishbone/image</c>: the image behind a variable marked with
/// <see cref="DebugSnapshotHandles.ImageKind"/>, encoded as PNG, and the shapes to draw over it.
/// Only valid while paused.
/// </summary>
[Method(FishboneImageArguments.Command, Direction.ClientToServer)]
public sealed record FishboneImageArguments : IRequest<FishboneImageResponse>
{
    public const string Command = "fishbone/image";

    public long VariablesReference { get; init; }
}

/// <summary>
/// The PNG bytes, base64 encoded since DAP messages are JSON, and empty for an image that's only
/// shapes. The shapes are in the coordinates of the image's own size, which the PNG can be
/// smaller than, since a big image is scaled down to send.
/// </summary>
public sealed record FishboneImageResponse
{
    public string Png { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public IReadOnlyList<FishboneRegion> Regions { get; init; } = [];
    public IReadOnlyList<FishboneContour> Contours { get; init; } = [];

    public static FishboneImageResponse From(FishboneImage image) => new()
    {
        Png = image.HasPixels ? Convert.ToBase64String(image.ToPng()) : string.Empty,
        Width = image.Width,
        Height = image.Height,
        Regions = image.Regions,
        Contours = image.Contours,
    };
}
