using System.Reflection;

namespace Fishbone.Engine;

public static class FishbonePluginLoader
{
    private static readonly object _sync = new();
    private static readonly List<string> _registeredPluginDirs = [];
    private static bool _resolverRegistered;

    public static string DefaultPluginsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".fishbone", "plugins");

    public static IReadOnlyList<string> LoadPlugins(string pluginsPath, FishboneConfiguration config)
    {
        var loaded = new List<string>();

        if (!Directory.Exists(pluginsPath))
            return loaded;

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
                    Console.Error.WriteLine($"Failed to load plugin assembly {dll}: {ex.Message}");
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
                        Console.Error.WriteLine($"Failed to initialize plugin {type.FullName} from {dll}: {ex.Message}");
                    }
                }
            }
        }

        return loaded;
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