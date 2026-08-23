// --------------------------------------------------------------------------------
// PluginInstaller.cs
//
// installs a plugin package into the plugins folder the loader scans.
//
// this is the piece that joins the two ways a plugin reaches a host. an app that embeds
// fishbone references a plugin's package and calls AddPlugin, and the compiler resolves
// everything. a standalone host has no project to add a reference to, so the same
// package has to be fetched and unpacked into a folder instead. one distribution
// channel, two ways in.
//
// dependency resolution here is a breadth-first walk of the dependency groups, taking
// the lowest version that satisfies each range, which is what nuget itself does for a
// direct install. it does not do full conflict resolution across a deep graph: plugin
// packages are shallow, and a wrong answer shows up as a missing assembly rather than
// silently. if that stops being true the answer is NuGet.Resolver, not more code here.
// --------------------------------------------------------------------------------

using System.Reflection;
using Fishbone.Core;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Fishbone.Hosting.NuGet;

public sealed class PluginInstaller
{
    /// <summary>The tag a package carries to be findable by <see cref="SearchAsync"/>.</summary>
    public const string PluginTag = "fishbone-plugin";

    private readonly string _pluginsDirectory;
    private readonly NuGetFramework _hostFramework;
    private readonly string _runtimeIdentifier;
    private readonly SourceCacheContext _cache = new();
    private readonly ILogger _logger = global::NuGet.Common.NullLogger.Instance;

    /// <param name="pluginsDirectory">
    /// Where plugins live, normally <see cref="FishbonePluginLoader.DefaultPluginsDirectory"/>.
    /// </param>
    /// <param name="hostFramework">
    /// The framework assets are chosen for. Defaults to the framework this process is running on,
    /// which is what matters: the assemblies have to load here, not somewhere else.
    /// </param>
    /// <param name="runtimeIdentifier">
    /// The platform native assets are chosen for. Defaults to this machine.
    /// </param>
    public PluginInstaller(
        string pluginsDirectory,
        NuGetFramework? hostFramework = null,
        string? runtimeIdentifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        _pluginsDirectory = pluginsDirectory;
        _hostFramework = hostFramework ?? CurrentFramework();
        _runtimeIdentifier = runtimeIdentifier ?? RuntimeIdentifiers.Current;
    }

    // --------------------------------------------------------------------------------
    // list and remove, which need no feed
    // --------------------------------------------------------------------------------

    /// <summary>
    /// What is installed, read from disk. A folder with no version marker still appears, with an
    /// empty version, because a plugin copied in by hand is still a plugin the loader will run.
    /// </summary>
    public IReadOnlyList<InstalledPlugin> List()
    {
        if (!Directory.Exists(_pluginsDirectory))
            return [];

        var installed = new List<InstalledPlugin>();

        foreach (string directory in Directory.EnumerateDirectories(_pluginsDirectory))
        {
            string id = Path.GetFileName(directory);
            installed.Add(new InstalledPlugin(id, ReadVersionMarker(directory) ?? string.Empty, directory)
            {
                Files = Directory.EnumerateFiles(directory)
                    .Select(Path.GetFileName)
                    .Where(name => name is not null && name != VersionMarkerFile)
                    .Select(name => name!)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            });
        }

        return installed;
    }

    /// <summary>
    /// Deletes a plugin's folder. Reports rather than throws when the folder is held open, which
    /// on Windows is what happens if a host has already loaded the plugin: the loader reaches it
    /// through Assembly.LoadFrom, and that keeps the file open for the life of the process.
    /// </summary>
    public PluginRemoveResult Remove(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        string directory = Path.Combine(_pluginsDirectory, packageId);
        if (!Directory.Exists(directory))
            return new PluginRemoveResult(false,
                [FishboneDiagnostics.Configuration($"Plugin \"{packageId}\" is not installed.")]);

        try
        {
            Directory.Delete(directory, recursive: true);
            return new PluginRemoveResult(true, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new PluginRemoveResult(false,
            [
                FishboneDiagnostics.Configuration(
                    $"Plugin \"{packageId}\" could not be removed: {exception.Message} " +
                    "If a Fishbone host is running, close it first: loading a plugin holds its " +
                    "files open until the process exits."),
            ]);
        }
    }

    // --------------------------------------------------------------------------------
    // install
    // --------------------------------------------------------------------------------

    /// <summary>
    /// Fetches <paramref name="packageId"/> and its dependencies and unpacks the parts that
    /// belong next to a plugin into its own folder under the plugins directory.
    /// </summary>
    /// <param name="packageId">The package to install.</param>
    /// <param name="version">An exact version, or null for the newest.</param>
    /// <param name="sources">Feed urls, or null to use the machine's configured sources.</param>
    /// <param name="includePrerelease">Whether a prerelease version may be chosen.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    public async Task<PluginInstallResult> InstallAsync(
        string packageId,
        string? version = null,
        IReadOnlyList<string>? sources = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        IReadOnlyList<SourceRepository> repositories = OpenRepositories(sources);
        if (repositories.Count == 0)
            return PluginInstallResult.Failed("No package sources are configured.");

        var diagnostics = new List<FishboneDiagnostic>();

        NuGetVersion? resolved = version is null
            ? await FindNewestAsync(packageId, repositories, includePrerelease, cancellationToken)
                .ConfigureAwait(false)
            : NuGetVersion.TryParse(version, out NuGetVersion? parsed) ? parsed : null;

        if (version is not null && resolved is null)
            return PluginInstallResult.Failed($"\"{version}\" is not a valid package version.");

        if (resolved is null)
            return PluginInstallResult.Failed(
                $"Package \"{packageId}\" was not found on any configured source" +
                (includePrerelease ? "." : ", or has only prerelease versions. Pass --prerelease to allow one."));

        // walk the graph before writing anything, so a missing dependency does not leave a
        // half-populated plugin folder behind
        var downloaded = new Dictionary<string, (NuGetVersion Version, MemoryStream Content)>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!await CollectAsync(packageId, resolved, repositories, downloaded, diagnostics,
                                    cancellationToken).ConfigureAwait(false))
                return PluginInstallResult.Failed(diagnostics);

            return Unpack(packageId, resolved, downloaded, diagnostics);
        }
        finally
        {
            foreach ((_, MemoryStream content) in downloaded.Values)
                await content.DisposeAsync().ConfigureAwait(false);
        }
    }

    // breadth-first over the dependency groups, one package downloaded at most once
    private async Task<bool> CollectAsync(
        string packageId,
        NuGetVersion version,
        IReadOnlyList<SourceRepository> repositories,
        Dictionary<string, (NuGetVersion Version, MemoryStream Content)> downloaded,
        List<FishboneDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var queue = new Queue<(string Id, NuGetVersion Version, bool Required)>();
        queue.Enqueue((packageId, version, true));

        while (queue.Count > 0)
        {
            (string id, NuGetVersion wanted, bool required) = queue.Dequeue();

            if (downloaded.ContainsKey(id))
                continue;

            // the engine's own packages are what the host already is. following them would
            // download Fishbone itself and then discard every file in it
            if (IsHostSuppliedPackage(id))
                continue;

            MemoryStream? content =
                await DownloadAsync(id, wanted, repositories, cancellationToken).ConfigureAwait(false);

            if (content is null)
            {
                string message = $"Could not download {id} {wanted}.";
                if (required)
                {
                    diagnostics.Add(FishboneDiagnostics.Configuration(message));
                    return false;
                }

                // a dependency that will not come down is reported and the install continues:
                // the plugin may well not need it on this platform
                diagnostics.Add(FishboneDiagnostics.Configuration(
                    message + " The plugin may fail to load if it needs it.",
                    DiagnosticSeverity.Warning));
                continue;
            }

            downloaded[id] = (wanted, content);

            foreach (PackageDependency dependency in ReadDependencies(content))
                if (!downloaded.ContainsKey(dependency.Id) && !IsHostSuppliedPackage(dependency.Id))
                    queue.Enqueue((dependency.Id, LowestSatisfying(dependency.VersionRange), false));
        }

        return true;
    }

    // the dependency group nearest the host framework, which is how nuget decides what a
    // package actually needs when installed here
    private IEnumerable<PackageDependency> ReadDependencies(MemoryStream content)
    {
        content.Position = 0;
        using var reader = new PackageArchiveReader(content, leaveStreamOpen: true);

        var groups = reader.NuspecReader.GetDependencyGroups().ToArray();
        if (groups.Length == 0)
            return [];

        var reducer = new FrameworkReducer();
        NuGetFramework? nearest = reducer.GetNearest(
            _hostFramework, groups.Select(group => group.TargetFramework));

        return nearest is null
            ? []
            : groups.First(group => group.TargetFramework.Equals(nearest)).Packages.ToArray();
    }

    private PluginInstallResult Unpack(
        string packageId,
        NuGetVersion version,
        Dictionary<string, (NuGetVersion Version, MemoryStream Content)> downloaded,
        List<FishboneDiagnostic> diagnostics)
    {
        string directory = Path.Combine(_pluginsDirectory, packageId);

        // a fresh folder, so an upgrade cannot leave an older assembly behind for the loader
        // to find beside the new one
        if (Directory.Exists(directory))
        {
            PluginRemoveResult removal = Remove(packageId);
            if (!removal.Removed)
                return PluginInstallResult.Failed(removal.Diagnostics);
        }

        Directory.CreateDirectory(directory);
        var written = new List<string>();

        foreach ((string id, (NuGetVersion _, MemoryStream content)) in downloaded)
        {
            content.Position = 0;
            using var reader = new PackageArchiveReader(content, leaveStreamOpen: true);
            string[] entries = reader.GetFiles().ToArray();

            PluginPackageContents contents =
                PluginPackageLayout.Select(entries, _hostFramework, _runtimeIdentifier);

            // only a genuine framework mismatch is worth saying out loud. a runtime package for
            // another platform, or one whose assets the framework already provides, contributing
            // nothing is the normal case and would otherwise warn on every successful install
            if (contents.EmptyReason == EmptyReason.Incompatible)
                diagnostics.Add(FishboneDiagnostics.Configuration(
                    $"{id} has no assemblies for {_hostFramework.GetShortFolderName()}, " +
                    "so the plugin may fail to load if it needs it.",
                    DiagnosticSeverity.Warning));

            foreach (PluginFile file in contents.Files)
            {
                string destination = Path.Combine(directory, file.FileName);
                using Stream source = reader.GetStream(file.PackageEntry);
                using FileStream target = File.Create(destination);
                source.CopyTo(target);
                written.Add(file.FileName);
            }
        }

        if (!written.Any(name => name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            // an empty plugin folder would be found by the loader, contain no plugin, and look
            // like a working install. better to leave nothing and say so
            Directory.Delete(directory, recursive: true);
            return PluginInstallResult.Failed(
                $"{packageId} {version} has no assemblies for " +
                $"{_hostFramework.GetShortFolderName()} on {_runtimeIdentifier}, so nothing was installed.");
        }

        WriteVersionMarker(directory, version);

        return new PluginInstallResult(
            new InstalledPlugin(packageId, version.ToNormalizedString(), directory)
            {
                Files = written.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            },
            diagnostics);
    }

    // --------------------------------------------------------------------------------
    // search
    // --------------------------------------------------------------------------------

    /// <summary>
    /// Finds packages matching <paramref name="term"/> that carry the
    /// <see cref="PluginTag"/> tag, which is how a third-party plugin becomes discoverable.
    /// </summary>
    public async Task<IReadOnlyList<PluginSearchHit>> SearchAsync(
        string term,
        IReadOnlyList<string>? sources = null,
        bool includePrerelease = false,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var hits = new List<PluginSearchHit>();

        foreach (SourceRepository repository in OpenRepositories(sources))
        {
            PackageSearchResource? search;
            try
            {
                search = await repository.GetResourceAsync<PackageSearchResource>(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            if (search is null)
                continue;

            IEnumerable<IPackageSearchMetadata> found;
            try
            {
                found = await search.SearchAsync(
                    $"{term} tag:{PluginTag}",
                    new SearchFilter(includePrerelease),
                    skip: 0, take: take, _logger, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            foreach (IPackageSearchMetadata metadata in found)
                hits.Add(new PluginSearchHit(
                    metadata.Identity.Id,
                    metadata.Identity.Version.ToNormalizedString(),
                    metadata.Description,
                    metadata.DownloadCount));
        }

        return hits;
    }

    // --------------------------------------------------------------------------------
    // feeds
    // --------------------------------------------------------------------------------

    private IReadOnlyList<SourceRepository> OpenRepositories(IReadOnlyList<string>? sources)
    {
        if (sources is { Count: > 0 })
            return sources.Select(Repository.Factory.GetCoreV3).ToArray();

        // whatever the machine is configured with, so a private feed in NuGet.config works
        // without the caller repeating it
        ISettings settings = Settings.LoadDefaultSettings(root: null);
        var provider = new PackageSourceProvider(settings);

        return provider.LoadPackageSources()
            .Where(source => source.IsEnabled)
            .Select(Repository.Factory.GetCoreV3)
            .ToArray();
    }

    private async Task<NuGetVersion?> FindNewestAsync(
        string packageId,
        IReadOnlyList<SourceRepository> repositories,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        NuGetVersion? newest = null;

        foreach (SourceRepository repository in repositories)
        {
            IEnumerable<NuGetVersion> versions;
            try
            {
                FindPackageByIdResource? resource = await repository
                    .GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);
                if (resource is null)
                    continue;

                versions = await resource
                    .GetAllVersionsAsync(packageId, _cache, _logger, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // an unreachable or misconfigured source must not stop the others being tried
                continue;
            }

            foreach (NuGetVersion version in versions)
            {
                if (version.IsPrerelease && !includePrerelease)
                    continue;
                if (newest is null || version > newest)
                    newest = version;
            }
        }

        return newest;
    }

    private async Task<MemoryStream?> DownloadAsync(
        string packageId,
        NuGetVersion version,
        IReadOnlyList<SourceRepository> repositories,
        CancellationToken cancellationToken)
    {
        foreach (SourceRepository repository in repositories)
        {
            var content = new MemoryStream();
            try
            {
                FindPackageByIdResource? resource = await repository
                    .GetResourceAsync<FindPackageByIdResource>(cancellationToken).ConfigureAwait(false);

                if (resource is not null && await resource.CopyNupkgToStreamAsync(
                        packageId, version, content, _cache, _logger, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return content;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await content.DisposeAsync().ConfigureAwait(false);
        }

        return null;
    }

    // --------------------------------------------------------------------------------
    // odds and ends
    // --------------------------------------------------------------------------------

    private const string VersionMarkerFile = ".fishbone-plugin-version";

    private static void WriteVersionMarker(string directory, NuGetVersion version) =>
        File.WriteAllText(Path.Combine(directory, VersionMarkerFile), version.ToNormalizedString());

    private static string? ReadVersionMarker(string directory)
    {
        string path = Path.Combine(directory, VersionMarkerFile);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    // the packages whose assemblies the host already has loaded
    internal static bool IsHostSuppliedPackage(string packageId) =>
        packageId.Equals("Fishbone", StringComparison.OrdinalIgnoreCase)
        || PluginPackageLayout.IsHostSuppliedAssembly(packageId + ".dll");

    // the lowest version satisfying a range, matching how nuget resolves a dependency
    internal static NuGetVersion LowestSatisfying(VersionRange range) =>
        range.MinVersion ?? range.MaxVersion ?? new NuGetVersion(0, 0, 0);

    // read off this assembly rather than hardcoded, so a host on a newer runtime gets assets
    // chosen for what it is actually running
    private static NuGetFramework CurrentFramework()
    {
        string? name = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
            ?.FrameworkName;

        return name is null ? NuGetFramework.Parse("net8.0") : NuGetFramework.ParseFrameworkName(name, DefaultFrameworkNameProvider.Instance);
    }
}
