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
/// Each registration reads the directories fresh, so a new configuration sees procedures that were
/// added or changed since the last one. HALCON's procedure path is process-wide: registering sets
/// it to these directories. Procedures loaded by earlier configurations keep working.
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
                throw new Exception($"Procedures directory '{directory}' does not exist; no procedures registered.");

        var procedures = _proceduresDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.hdvp"))
            // procedures dont need to be snake_cased unlike operators
            .Select(path => (Name: Path.GetFileNameWithoutExtension(path), Path: path))
            .ToList();

        // a clash is a setup mistake, so it fails before anything is registered. renaming the
        // procedure and building a new configuration picks up the change. the same name in two
        // directories clashes too, since halcon would just take the first one on the path
        var clashes = procedures
            .GroupBy(procedure => procedure.Name)
            .Where(group => group.Count() > 1 || config.BuiltIns.ContainsKey(group.Key))
            .Select(group => group.Key)
            .ToList();
        if (clashes.Count > 0)
            throw new Exception($"Procedures in '{string.Join("', '", _proceduresDirectories)}' have names that are already taken: {string.Join(", ", clashes)}. Rename them.");

        lock (EngineLock)
        {
            _engine ??= new HDevEngine();
            // setting the path rescans the folders. everything is unloaded before anything loads,
            // so a procedure doesn't pick up a stale copy of a helper it calls
            _engine.SetProcedurePath(string.Join(Path.PathSeparator, _proceduresDirectories));
            foreach (var procedure in procedures)
                _engine.UnloadProcedure(procedure.Name);

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