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
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    foreach (var type in assembly.GetExportedTypes())
                    {
                        if (!type.IsClass || type.IsAbstract || !typeof(IFishbonePlugin).IsAssignableFrom(type))
                            continue;

                        if (Activator.CreateInstance(type) is IFishbonePlugin plugin)
                        {
                            plugin.Register(config);
                            loaded.Add($"{plugin.GetType().Name} ({dll})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to load plugin from {dll}: {ex.Message}");
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