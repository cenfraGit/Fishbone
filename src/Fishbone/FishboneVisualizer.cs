using Fishbone.Debugging;

namespace Fishbone;

/// <summary>A registered way to show values as images. See <see cref="FishboneConfiguration.AddVisualizer"/>.</summary>
public sealed record FishboneVisualizer(Func<object, FishboneImage?> ToImage, Func<object, bool>? CanShow)
{
    /// <summary>The images a value holds, when it holds several. See <see cref="FishboneConfiguration.AddVisualizer"/>.</summary>
    public Func<object, IReadOnlyList<(string Name, object? Value)>?>? Children { get; init; }
}
