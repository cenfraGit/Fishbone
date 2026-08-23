// --------------------------------------------------------------------------------
// PluginInstallResults.cs
//
// what an install, a listing, or a removal hands back.
//
// nothing here throws for an ordinary failure. a feed that is unreachable, a package
// that does not exist, a package with nothing for this platform: all of those are
// results a host renders, the same way FishbonePluginLoader already reports a plugin it
// could not load. an exception is reserved for a broken caller.
// --------------------------------------------------------------------------------

using Fishbone.Core;

namespace Fishbone.Hosting.NuGet;

/// <summary>A plugin present in the plugins directory.</summary>
public sealed record InstalledPlugin(string PackageId, string Version, string Directory)
{
    /// <summary>File names in the plugin's folder, without paths.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];
}

/// <summary>The outcome of an install.</summary>
public sealed record PluginInstallResult(
    InstalledPlugin? Installed,
    IReadOnlyList<FishboneDiagnostic> Diagnostics)
{
    /// <summary>Whether the plugin is now on disk and ready for the loader to find.</summary>
    public bool Succeeded => Installed is not null;

    internal static PluginInstallResult Failed(string message) =>
        new(null, [FishboneDiagnostics.Configuration(message)]);

    internal static PluginInstallResult Failed(IReadOnlyList<FishboneDiagnostic> diagnostics) =>
        new(null, diagnostics);
}

/// <summary>The outcome of a removal.</summary>
public sealed record PluginRemoveResult(
    bool Removed,
    IReadOnlyList<FishboneDiagnostic> Diagnostics);

/// <summary>One package found by a search.</summary>
public sealed record PluginSearchHit(
    string PackageId,
    string Version,
    string? Description,
    long? Downloads);
