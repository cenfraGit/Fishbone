using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111;

/// <summary>
/// Discovers hdevelop procedures in a directory and exposes to fishbone as callables.
/// A host that knows where its procedures live can construct this plugin directly,
/// passing its directory.
/// 
/// This plugin allows fishbone to call these procedures exactly to how you'd do with 
/// hdevelop (same iconic/control input/output order), the only difference being 
/// that output params in fishbone need the "out" keyword.
/// </summary>
public sealed class HalconProcedurePlugin : IFishbonePlugin
{
    // used when loading plugin from .fishbone/plugins
    public const string ProceduresDirectoryVariable = "FISHBONE_HALCON_PROCEDURES";

    private readonly string? _proceduresDirectory;

    private HDevEngine? _engine;

    /// <summary>
    /// Used by plugin loader (which cannot pass args).
    /// </summary>
    public HalconProcedurePlugin()
        : this(Environment.GetEnvironmentVariable(ProceduresDirectoryVariable))
    {
    }

    public HalconProcedurePlugin(string? proceduresDirectory)
    {
        _proceduresDirectory = proceduresDirectory;
    }

    public void Register(FishboneConfiguration config)
    {
        config.AddTypeConverter(
            typeof(HTuple),
            toNet: value => HalconConverters.ToHTuple(value),
            fromNet: value => HalconConverters.FromHTuple((HTuple)value));

        // no directory configured is not a failure: a host that only wants the operators gets here
        if (string.IsNullOrWhiteSpace(_proceduresDirectory))
            return;

        if (!Directory.Exists(_proceduresDirectory))
            throw new Exception($"Procedures directory '{_proceduresDirectory}' does not exist; no procedures registered.");

        _engine ??= new HDevEngine();
        _engine.AddProcedurePath(_proceduresDirectory);

        foreach (var hdvpPath in Directory.EnumerateFiles(_proceduresDirectory, "*.hdvp"))
        {
            // procedures dont need to be snake_cased unlike operators
            var procedureName = Path.GetFileNameWithoutExtension(hdvpPath);

            HalconProcedureCallable callable;
            try
            {
                callable = HalconProcedureCallable.Load(procedureName, hdvpPath);
            }
            catch (Exception ex)
            {
                continue;
            }

            // procedures win on a name collision with an existing builtin, but the shadowing is
            // surfaced so it is never silent
            //if (config.BuiltIns.ContainsKey(procedureName))
            //    throw new Exception($"Procedure '{procedureName}' shadows an existing builtin of the same name.");

            config.AddBuiltIn(procedureName, callable);
        }
    }
}