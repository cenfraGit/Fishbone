// --------------------------------------------------------------------------------
// PluginPackageLayout.cs
//
// decides which files inside a nupkg belong in a plugin folder.
//
// a nupkg is laid out for a compiler and a publish step, not for a folder someone
// drops assemblies into. it carries several target frameworks, per-platform native
// binaries, msbuild props, reference-only assemblies, signatures. exactly one slice of
// that is what the plugin loader needs sitting next to the plugin dll.
//
// this file is deliberately free of I/O so the choosing can be tested against real
// package layouts without a feed or a disk.
// --------------------------------------------------------------------------------

using NuGet.Frameworks;

namespace Fishbone.Hosting.NuGet;

/// <summary>A file to take out of a package, and the name it gets in the plugin folder.</summary>
internal readonly record struct PluginFile(string PackageEntry, string FileName);

/// <summary>Why a package contributed no files, which decides whether that is worth reporting.</summary>
internal enum EmptyReason
{
    /// <summary>Files were selected, so there is nothing to explain.</summary>
    NotEmpty,

    /// <summary>
    /// The package carries no runnable assets for any framework or platform at all. A native
    /// runtime package for another operating system looks like this, and so does a package that
    /// exists only to pull in dependencies. Expected, not worth a warning.
    /// </summary>
    NothingToInstall,

    /// <summary>
    /// The matching framework folder holds only NuGet's <c>_._</c> placeholder, which is how a
    /// package says the framework already provides this. Expected, not worth a warning.
    /// </summary>
    ProvidedByFramework,

    /// <summary>
    /// The package has assemblies, but for frameworks or platforms this host is not one of. This
    /// is the case worth telling somebody about.
    /// </summary>
    Incompatible,
}

/// <summary>What to install out of one package.</summary>
internal readonly record struct PluginPackageContents(
    IReadOnlyList<PluginFile> Files,
    EmptyReason EmptyReason);

internal static class PluginPackageLayout
{
    // the engine's own assemblies. the host already has these loaded, and a second copy in a
    // plugin folder is the bug the loader's AssemblyResolve handler would walk straight into:
    // it would bind IFishbonePlugin to a different type than the host checks against, and the
    // plugin would silently never be recognised.
    //
    // an explicit list rather than a "Fishbone.*" prefix test, because a plugin is itself
    // usually called Fishbone.Plugins.Something and must obviously not be skipped.
    private static readonly HashSet<string> HostSuppliedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fishbone.Core",
        "Fishbone.Debugging",
        "Fishbone.DebugAdapter",
        "Fishbone.DebugClient",
        "Fishbone.Engine",
        "Fishbone.Hosting",
        "Fishbone.Hosting.NuGet",
        "Fishbone.Interpreter",
        "Fishbone.Parser",
    };

    // what is worth copying next to a plugin. a .pdb is included so a stack trace out of a
    // plugin names lines; everything else in a package is for building, not running.
    private static readonly HashSet<string> RunnableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".xml", ".pdb", ".so", ".dylib",
    };

    /// <summary>
    /// Whether an assembly file name is one the host supplies, and must therefore not be copied
    /// into a plugin folder.
    /// </summary>
    public static bool IsHostSuppliedAssembly(string fileName) =>
        HostSuppliedAssemblies.Contains(Path.GetFileNameWithoutExtension(fileName));

    /// <summary>
    /// Picks the files to install out of <paramref name="packageEntries"/>, which are the paths
    /// inside a nupkg using forward slashes.
    /// </summary>
    /// <param name="packageEntries">Paths inside the nupkg.</param>
    /// <param name="hostFramework">
    /// The framework the host runs on, used to choose among the package's <c>lib</c> folders.
    /// This is the host's framework and not the plugin's: the assemblies have to load into the
    /// running process.
    /// </param>
    /// <param name="runtimeIdentifier">
    /// The platform to take native and platform-specific assets for, for example
    /// <c>win-x64</c>.
    /// </param>
    public static PluginPackageContents Select(
        IEnumerable<string> packageEntries,
        NuGetFramework hostFramework,
        string runtimeIdentifier)
    {
        ArgumentNullException.ThrowIfNull(packageEntries);
        ArgumentNullException.ThrowIfNull(hostFramework);

        string[] entries = packageEntries
            .Select(entry => entry.Replace('\\', '/'))
            .ToArray();

        var selected = new List<PluginFile>();
        var takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // rid-specific managed assemblies win over the framework-only ones, so they are chosen
        // first and the lib pass then skips any name already taken
        foreach (PluginFile file in SelectRuntimeLib(entries, hostFramework, runtimeIdentifier))
            if (takenNames.Add(file.FileName))
                selected.Add(file);

        foreach (PluginFile file in SelectLib(entries, hostFramework))
            if (takenNames.Add(file.FileName))
                selected.Add(file);

        foreach (PluginFile file in SelectNative(entries, runtimeIdentifier))
            if (takenNames.Add(file.FileName))
                selected.Add(file);

        if (selected.Count > 0)
            return new PluginPackageContents(selected, EmptyReason.NotEmpty);

        return new PluginPackageContents(selected, ExplainEmpty(entries, hostFramework));
    }

    // an empty result is normal often enough that saying so unconditionally would put two
    // warnings on every successful OpenCV install
    private static EmptyReason ExplainEmpty(string[] entries, NuGetFramework hostFramework)
    {
        bool hasFrameworkFolders = false;

        foreach (string entry in entries)
        {
            if (!entry.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
                continue;

            hasFrameworkFolders = true;
            string[] parts = entry.Split('/');
            if (parts.Length == 3 && parts[2] == "_._")
            {
                NuGetFramework framework = NuGetFramework.ParseFolder(parts[1]);
                if (!framework.IsUnsupported
                    && DefaultCompatibilityProvider.Instance.IsCompatible(hostFramework, framework))
                {
                    return EmptyReason.ProvidedByFramework;
                }
            }
        }

        return hasFrameworkFolders ? EmptyReason.Incompatible : EmptyReason.NothingToInstall;
    }

    // lib/<tfm>/... , reduced to the one folder nearest the host's framework
    private static IEnumerable<PluginFile> SelectLib(string[] entries, NuGetFramework hostFramework) =>
        SelectFromFrameworkFolders(entries, hostFramework, prefix: "lib/", depth: 3);

    // runtimes/<rid>/lib/<tfm>/... , the platform-specific managed build of an assembly
    private static IEnumerable<PluginFile> SelectRuntimeLib(
        string[] entries, NuGetFramework hostFramework, string runtimeIdentifier)
    {
        foreach (string rid in RuntimeIdentifiers.FallbackChain(runtimeIdentifier))
        {
            var chosen = SelectFromFrameworkFolders(
                entries, hostFramework, prefix: $"runtimes/{rid}/lib/", depth: 5).ToArray();

            // first rid in the chain that has anything wins; falling further would mix platforms
            if (chosen.Length > 0)
                return chosen;
        }

        return [];
    }

    // shared by lib/ and runtimes/<rid>/lib/: group the entries by their framework folder, ask
    // NuGet which is nearest, and take the runnable files out of that one
    private static IEnumerable<PluginFile> SelectFromFrameworkFolders(
        string[] entries, NuGetFramework hostFramework, string prefix, int depth)
    {
        var byFramework = new Dictionary<NuGetFramework, List<string>>();

        foreach (string entry in entries)
        {
            if (!entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string[] parts = entry.Split('/');
            // depth is the segment count for a file sitting directly in the framework folder:
            // lib/<tfm>/<file> is 3, runtimes/<rid>/lib/<tfm>/<file> is 5. anything longer is a
            // subfolder, which for lib/ means satellite resources we do not want
            if (parts.Length != depth)
                continue;

            NuGetFramework framework = NuGetFramework.ParseFolder(parts[depth - 2]);
            if (framework.IsUnsupported)
                continue;

            if (!byFramework.TryGetValue(framework, out List<string>? group))
                byFramework[framework] = group = [];
            group.Add(entry);
        }

        if (byFramework.Count == 0)
            return [];

        var reducer = new FrameworkReducer();
        NuGetFramework? nearest = reducer.GetNearest(hostFramework, byFramework.Keys);
        if (nearest is null)
            return [];

        return Runnable(byFramework[nearest]);
    }

    // runtimes/<rid>/native/... , flattened next to the plugin so the default native probing
    // finds it. keeping the runtimes/ tree would only work with a deps.json describing it,
    // which an installed plugin folder has no way to produce
    private static IEnumerable<PluginFile> SelectNative(string[] entries, string runtimeIdentifier)
    {
        foreach (string rid in RuntimeIdentifiers.FallbackChain(runtimeIdentifier))
        {
            string prefix = $"runtimes/{rid}/native/";
            var chosen = Runnable(entries.Where(
                entry => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray();

            if (chosen.Length > 0)
                return chosen;
        }

        return [];
    }

    private static IEnumerable<PluginFile> Runnable(IEnumerable<string> entries)
    {
        foreach (string entry in entries)
        {
            string fileName = entry[(entry.LastIndexOf('/') + 1)..];

            if (fileName.Length == 0)
                continue;
            if (!RunnableExtensions.Contains(Path.GetExtension(fileName)))
                continue;
            // a reference assembly has no implementation, and _._ is nuget's empty placeholder
            if (fileName == "_._")
                continue;
            if (IsHostSuppliedAssembly(fileName))
                continue;

            yield return new PluginFile(entry, fileName);
        }
    }
}
