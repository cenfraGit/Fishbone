using Fishbone.Interpreter;
using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111;

/// <summary>
/// Discovers hdevelop procedures in one or more directories and exposes them to fishbone as
/// callables. A host that knows where its procedures live can construct this plugin directly,
/// passing its directories. A procedure can call procedures from any of them.
/// 
/// This plugin allows fishbone to call these procedures exactly to how you'd do with 
/// hdevelop (same iconic/control input/output order), the only difference being 
/// that output params in fishbone need the "out" keyword.
///
/// Registering adds the directories to HALCON's procedure path, which is process-wide, and loads
/// the procedures in them. A procedure HALCON already loaded is reused, so registering again is
/// cheap, but it doesn't pick up files added or edited on disk since. When the files changed,
/// call <see cref="Reload"/> and then build a new configuration. HALCON knows procedures by name
/// across the whole process, so two folders with a procedure of the same name also need a
/// <see cref="Reload"/> to switch between them.
/// </summary>
public sealed class HalconProcedurePlugin : IFishbonePlugin
{
    /// <summary>
    /// Read by the parameterless constructor, which the plugin loader uses. Several directories
    /// are separated like PATH, with <see cref="Path.PathSeparator"/>.
    /// </summary>
    public const string ProceduresDirectoryVariable = "FISHBONE_HALCON_PROCEDURES";

    private readonly string[] _proceduresDirectories;

    // the procedure path is process-wide, so two registrations must not interleave
    private static readonly object EngineLock = new();

    private HDevEngine? _engine;

    /// <summary>
    /// Makes HALCON pick up procedures that were added or edited on disk since they were loaded.
    /// Sets the procedure path to exactly these directories, which finds new files, and unloads
    /// every loaded procedure, so the next load reads the edited files. Call it when the files
    /// changed, then build a new configuration. Configurations built before keep working.
    /// </summary>
    public static void Reload(params string[] proceduresDirectories)
    {
        lock (EngineLock)
        {
            // the path and the loaded procedures belong to the process, not to an engine
            var engine = new HDevEngine();
            engine.SetProcedurePath(string.Join(Path.PathSeparator, proceduresDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory))));
            engine.UnloadAllProcedures();
        }
    }

    /// <summary>
    /// Used by plugin loader (which cannot pass args).
    /// </summary>
    public HalconProcedurePlugin()
        : this((Environment.GetEnvironmentVariable(ProceduresDirectoryVariable) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
    }

    public HalconProcedurePlugin(params string[] proceduresDirectories)
    {
        _proceduresDirectories = proceduresDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory)).ToArray();
    }

    public void Register(FishboneConfiguration config)
    {
        config.AddTypeConverter(
            typeof(HTuple),
            toNet: value => HalconConverters.ToHTuple(value),
            fromNet: value => HalconConverters.FromHTuple((HTuple)value));
        HalconVisualizer.Register(config);

        // no directory configured is not a failure: a host that only wants the operators gets here
        if (_proceduresDirectories.Length == 0)
            return;

        foreach (var directory in _proceduresDirectories)
            if (!Directory.Exists(directory))
                throw new FishboneConfigurationException($"Procedures directory '{directory}' does not exist; no procedures registered.");

        var procedures = _proceduresDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.hdvp"))
            // procedures dont need to be snake_cased unlike operators
            .Select(path => (Name: Path.GetFileNameWithoutExtension(path), Path: path))
            .ToList();

        // a clash is a setup mistake, so it fails before anything is registered. renaming the
        // procedure, calling Reload and building a new configuration picks up the change. the same name in two
        // directories clashes too, since halcon would just take the first one on the path
        var clashes = procedures
            .GroupBy(procedure => procedure.Name)
            .Where(group => group.Count() > 1 || config.BuiltIns.ContainsKey(group.Key))
            .Select(group => group.Key)
            .ToList();
        if (clashes.Count > 0)
            throw new FishboneConfigurationException(
                $"Procedures in '{string.Join("', '", _proceduresDirectories)}' have names that are already taken: {string.Join(", ", clashes)}. Rename them.",
                clashes);

        lock (EngineLock)
        {
            _engine ??= new HDevEngine();
            // adding a directory that's already on the path does nothing, and a procedure that's
            // loaded stays as it is. Reload is what reads changed files
            foreach (var directory in _proceduresDirectories)
                _engine.AddProcedurePath(directory);

            foreach (var (name, path) in procedures)
            {
                IManualCallable callable;
                try
                {
                    callable = HalconProcedureCallable.Load(name, path);
                }
                catch (Exception ex)
                {
                    // the other procedures still load. this one says why when it's called
                    callable = HalconProcedureCallable.Failed(path, ex.Message);
                }
                config.AddBuiltIn(name, callable);
            }
        }
    }
}