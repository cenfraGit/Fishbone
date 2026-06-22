using System.Reflection;
using Fishbone.Core;
using Fishbone.Engine;
using Fishbone.Interpreter;
using System.CommandLine;

namespace SpineCLI;

internal class Program
{
    private static readonly HashSet<string> _pluginDirs = [];
    private static readonly List<string> _loadedPlugins = [];

    private static int Main(string[] args)
    {
        // ------------------------------------------------------------ arguments

        var argumentScript = new Argument<FileInfo>(name: "scriptPath")
        {
            Description = "The path to the fishbone script to execute.",
            Arity = ArgumentArity.ExactlyOne,
        };
        argumentScript.AcceptExistingOnly(); // file must exist

        // ------------------------------------------------------------ options

        var optionShowValues = new Option<bool>("--show-values")
        {
            Description = "Display script variables after execution."
        };

        var optionShowBuiltins = new Option<bool>("--show-builtins")
        {
            Description = "Display built-in functions and values after execution."
        };

        var optionShowPlugins = new Option<bool>("--show-plugins")
        {
            Description = "Display loaded plugins."
        };

        var rootCommand = new RootCommand("Fishbone script runner.");
        rootCommand.Add(argumentScript);
        rootCommand.Add(optionShowValues);
        rootCommand.Add(optionShowBuiltins);
        rootCommand.Add(optionShowPlugins);

        rootCommand.SetAction(parseResult =>
        {
            FileInfo scriptFile = parseResult.GetValue(argumentScript)!;
            bool showValues = parseResult.GetValue(optionShowValues);
            bool showBuiltins = parseResult.GetValue(optionShowBuiltins);
            bool showPlugins = parseResult.GetValue(optionShowPlugins);

            ExecuteScript(scriptFile, showValues, showBuiltins, showPlugins);
        });

        ParseResult parseResult = rootCommand.Parse(args);

        return parseResult.Invoke();
    }

    static void ExecuteScript(FileInfo script, bool showValues, bool showBuiltins, bool showPlugins)
    {
        if (!File.Exists(script.FullName))
            throw new FileNotFoundException(script.FullName);

        var contents = File.ReadAllText(script.FullName);

        var config = new FishboneConfiguration();

        // will look for plguins in plugins/*/ dirs. where IFishbonePlugins could live
        var pluginsPath = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (Directory.Exists(pluginsPath))
            LoadPlugins(pluginsPath, config);

        FishboneEnvironment env;
        try
        {
            env = FishboneEngine.Run(contents, config);
        }
        catch (FishboneRuntimeException ex)
        {
            if (ex.Line > 0)
                Console.Error.WriteLine($"Error at line {ex.Line}, column {ex.Column}: {ex.Message}");
            else
                Console.Error.WriteLine($"Error: {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return;
        }

        if (showValues)
        {
            Console.WriteLine("\nVALUES:");
            foreach (var variable in env.Values)
                Console.WriteLine($"{variable.Key} : {variable.Value}");
        }

        if (showBuiltins)
        {
            Console.WriteLine("\nBUILT-INS:");
            foreach (var builtin in config.BuiltIns.OrderBy(kvp => kvp.Key))
                Console.WriteLine($"{builtin.Key} : {builtin.Value}");
        }

        if (showPlugins)
        {
            Console.WriteLine("\nLOADED PLUGINS:");
            if (_loadedPlugins.Count == 0)
                Console.WriteLine("(none)");
            else
                foreach (var plugin in _loadedPlugins)
                    Console.WriteLine(plugin);
        }
    }

    static void LoadPlugins(string pluginsPath, FishboneConfiguration config)
    {
        foreach (var dir in Directory.EnumerateDirectories(pluginsPath))
            _pluginDirs.Add(dir);

        AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginAssembly;

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
                            _loadedPlugins.Add($"{plugin.GetType().Name} ({dll})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to load plugin from {dll}: {ex.Message}");
                }
            }
        }
    }

    static Assembly? ResolvePluginAssembly(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name + ".dll";

        foreach (var dir in _pluginDirs)
        {
            var path = Path.Combine(dir, assemblyName);
            if (File.Exists(path))
                return Assembly.LoadFrom(path);
        }

        return null;
    }
}