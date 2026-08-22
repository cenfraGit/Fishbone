// --------------------------------------------------------------------------------
// FishbonePluginLoader.cs
//
// scans the .fishbone/plugins folder for plugin DLLs and loads them automatically.
//
// loading is best effort: a plugin that cannot load is skipped rather than taking
// the whole run down, so the failures have to be reported somehow. this used to be
// Console.Error.WriteLine straight from the loader, which works for a console host
// and silently drops the message for a gui one, where there is no console attached.
//
// Load returns the failures as diagnostics and prints nothing. LoadPlugins keeps the
// old console behavior for callers that want it, but now it is the caller's choice
// rather than the library's.
// --------------------------------------------------------------------------------

using System.Reflection;
using Fishbone.Core;

namespace Fishbone.Engine;

/// <summary>The outcome of a plugin scan: what loaded, and what went wrong doing it.</summary>
public sealed record PluginLoadResult(
    IReadOnlyList<string> Loaded,
    IReadOnlyList<FishboneDiagnostic> Diagnostics);

public static class FishbonePluginLoader
{
    private static readonly object _sync = new();
    private static readonly List<string> _registeredPluginDirs = [];
    private static bool _resolverRegistered;

    public static string DefaultPluginsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".fishbone", "plugins");

    /// <summary>
    /// Scans <paramref name="pluginsPath"/> and registers every plugin it can load. Writes the
    /// failures to <see cref="Console.Error"/>, which suits a console host; a host without a
    /// console should call <see cref="Load"/> and render the diagnostics itself.
    /// </summary>
    public static IReadOnlyList<string> LoadPlugins(string pluginsPath, FishboneConfiguration config)
    {
        var result = Load(pluginsPath, config);

        foreach (var diagnostic in result.Diagnostics)
            Console.Error.WriteLine(diagnostic.Message);

        return result.Loaded;
    }

    /// <summary>
    /// Scans <paramref name="pluginsPath"/> and registers every plugin it can load, reporting
    /// anything that failed as configuration diagnostics instead of writing to the console. A
    /// plugin that cannot be loaded or initialized is skipped rather than aborting the scan, so
    /// a nonempty <see cref="PluginLoadResult.Diagnostics"/> does not mean nothing loaded.
    /// </summary>
    public static PluginLoadResult Load(string pluginsPath, FishboneConfiguration config)
    {
        var loaded = new List<string>();
        var diagnostics = new List<FishboneDiagnostic>();

        if (!Directory.Exists(pluginsPath))
            return new PluginLoadResult(loaded, diagnostics);

        EnsureAssemblyResolverRegistered(pluginsPath);

        foreach (var dir in Directory.EnumerateDirectories(pluginsPath))
        {
            foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
            {
                Type[] exportedTypes;
                try
                {
                    exportedTypes = Assembly.LoadFrom(dll).GetExportedTypes();
                }
                catch (Exception ex)
                {
                    // a DLL that can't be loaded or whose types can't be resolved (missing dependency,
                    // wrong architecture, etc.) is skipped whole rather than taking the loader down
                    diagnostics.Add(FishboneDiagnostics.Configuration(
                        $"Failed to load plugin assembly {dll}: {ex.Message}"));
                    continue;
                }

                foreach (var type in exportedTypes)
                {
                    if (!type.IsClass || type.IsAbstract || !typeof(IFishbonePlugin).IsAssignableFrom(type))
                        continue;

                    // folder-loaded plugins must be constructible with no arguments. A plugin type that
                    // needs constructor arguments is meant to be created directly by a host
                    if (type.GetConstructor(Type.EmptyTypes) is null)
                        continue;

                    try
                    {
                        if (Activator.CreateInstance(type) is IFishbonePlugin plugin)
                        {
                            plugin.Register(config);
                            loaded.Add($"{plugin.GetType().Name} ({dll})");
                        }
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add(FishboneDiagnostics.Configuration(
                            $"Failed to initialize plugin {type.FullName} from {dll}: {ex.Message}"));
                    }
                }
            }
        }

        return new PluginLoadResult(loaded, diagnostics);
    }

    private static void EnsureAssemblyResolverRegistered(string pluginsPath)
    {
        lock (_sync)
        {
            _registeredPluginDirs.AddRange(Directory.EnumerateDirectories(pluginsPath));
            if (_resolverRegistered)
                return;
            _resolverRegistered = true;

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var assemblyName = new AssemblyName(args.Name).Name + ".dll";
                foreach (var dir in _registeredPluginDirs)
                {
                    var path = Path.Combine(dir, assemblyName);
                    if (File.Exists(path))
                        return Assembly.LoadFrom(path);
                }
                return null;
            };
        }
    }
}