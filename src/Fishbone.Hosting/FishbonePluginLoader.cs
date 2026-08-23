// --------------------------------------------------------------------------------
// FishbonePluginLoader.cs
//
// scans the .fishbone/plugins folder for plugin DLLs and loads them automatically.
//
// this lives in Fishbone.Hosting rather than Fishbone.Engine on purpose. discovery is
// host policy: it names a directory under the user's profile and installs a
// process-wide AssemblyResolve handler, neither of which an app that merely embeds
// fishbone should inherit by referencing it. an embedding host calls
// config.AddPlugin(new SomePlugin()) and never comes near this file.
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
using System.Reflection.PortableExecutable;
using Fishbone.Core;
using Fishbone.Engine;

namespace Fishbone.Hosting;

/// <summary>The outcome of a plugin scan: what loaded, and what went wrong doing it.</summary>
public sealed record PluginLoadResult(
    IReadOnlyList<string> Loaded,
    IReadOnlyList<FishboneDiagnostic> Diagnostics);

public static class FishbonePluginLoader
{
    private static readonly object _sync = new();
    private static readonly List<string> _registeredPluginDirs = [];
    private static bool _resolverRegistered;

    // the engine a discovered plugin has to agree with, read from whichever assembly
    // IFishbonePlugin actually came from rather than hardcoding a name
    private static readonly AssemblyName EngineAssembly = typeof(IFishbonePlugin).Assembly.GetName();

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
                // a native library beside the plugin is normal (OpenCV installs
                // OpenCvSharpExtern.dll next to its own assembly), and reporting each one as a
                // failed plugin was noise. a file that is not a portable executable at all is a
                // different matter and still gets reported
                if (Classify(dll) == DllKind.NativeLibrary)
                    continue;

                Assembly assembly;
                try
                {
                    assembly = Assembly.LoadFrom(dll);
                }
                catch (Exception ex)
                {
                    // a DLL that can't be loaded (not managed, wrong architecture, built for a
                    // newer .NET) is skipped whole rather than taking the loader down
                    diagnostics.Add(FishboneDiagnostics.Configuration(
                        $"Failed to load plugin assembly {dll}: {ex.Message}"));
                    continue;
                }

                // checked before touching the types: a plugin built against a different major
                // version of the engine fails with a TypeLoadException naming an internal type,
                // which tells whoever installed it nothing. GetReferencedAssemblies reads the
                // metadata without resolving anything, so it is safe this early
                if (FindEngineReference(assembly) is { } engineReference
                    && !IsEngineVersionCompatible(engineReference, EngineAssembly))
                {
                    diagnostics.Add(FishboneDiagnostics.Configuration(
                        $"Plugin {Path.GetFileName(dll)} was built against Fishbone " +
                        $"{engineReference.Version} but this host runs {EngineAssembly.Version}, " +
                        "and was skipped. Install a build of the plugin that matches."));
                    continue;
                }

                Type[] exportedTypes;
                try
                {
                    exportedTypes = assembly.GetExportedTypes();
                }
                catch (Exception ex)
                {
                    // the assembly loaded but its public types cannot be resolved, which usually
                    // means a dependency is missing next to it
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
                            // through AddPlugin rather than Register directly, so a discovered
                            // plugin and a referenced one go down exactly one code path. what the
                            // plugin reports joins the loader's own diagnostics
                            config.AddPlugin(plugin, diagnostics.Add);
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

    private enum DllKind
    {
        /// <summary>Carries CLI metadata, so it may hold a plugin.</summary>
        Managed,

        /// <summary>A valid binary with no CLI metadata: a native library, which is expected.</summary>
        NativeLibrary,

        /// <summary>Not a binary at all, which is worth telling somebody about.</summary>
        NotAPortableExecutable,
    }

    /// <summary>
    /// Reads just enough of the file's header to tell a native library apart from a managed
    /// assembly, without loading anything. Distinguishing those two is what lets the loader stay
    /// quiet about the first and still report the second.
    /// </summary>
    private static DllKind Classify(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.HasMetadata ? DllKind.Managed : DllKind.NativeLibrary;
        }
        catch (BadImageFormatException)
        {
            return DllKind.NotAPortableExecutable;
        }
        catch (IOException)
        {
            // unreadable for some other reason. treat it as managed so Assembly.LoadFrom
            // produces the real message rather than this guessing at one
            return DllKind.Managed;
        }
    }

    /// <summary>
    /// The plugin's reference to the engine assembly, or null when it does not reference it at
    /// all. A DLL that sits in a plugins folder without referencing the engine is usually a
    /// dependency rather than a plugin, so there is nothing to check.
    /// </summary>
    private static AssemblyName? FindEngineReference(Assembly assembly)
    {
        foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            if (string.Equals(reference.Name, EngineAssembly.Name, StringComparison.OrdinalIgnoreCase))
                return reference;

        return null;
    }

    /// <summary>
    /// Whether a plugin built against <paramref name="pluginReference"/> can run on
    /// <paramref name="host"/>. Major version only: that is what semver says may break, and a
    /// plugin built against an older minor is expected to keep working. Errs towards loading when
    /// either version is missing, so an unversioned build fails loudly on its own terms rather
    /// than being refused on a guess.
    /// </summary>
    internal static bool IsEngineVersionCompatible(AssemblyName pluginReference, AssemblyName host)
    {
        if (pluginReference.Version is null || host.Version is null)
            return true;

        return pluginReference.Version.Major == host.Version.Major;
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