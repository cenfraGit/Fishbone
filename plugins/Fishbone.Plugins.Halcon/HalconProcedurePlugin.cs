using Fishbone.Engine;
using HalconDotNet;

namespace Fishbone.Plugins.Halcon;

/// <summary>
/// Discovers the HDevelop procedures (<c>*.hdvp</c>) in a directory and exposes each as a
/// script-callable that uses native out syntax, e.g. <c>my_proc(img, 10, out region, out area)</c>.
/// This complements <see cref="HalconOperatorPlugin"/> (which exposes HALCON operators); together a
/// script can call operators and its own procedures with identical HDevelop-style syntax.
/// </summary>
/// <remarks>
/// A host that knows where its procedures live should construct this directly and pass the
/// directory. When the plugin is picked up from the plugins folder instead, the loader can only
/// call a parameterless constructor, so the directory comes from the
/// <c>FISHBONE_HALCON_PROCEDURES</c> environment variable. With that variable unset there are no
/// procedures to register and the plugin does nothing, which is the normal case for a host that
/// only wants the operators.
/// </remarks>
public sealed class HalconProcedurePlugin : IFishbonePlugin
{
    /// <summary>Names the procedures directory when the plugin is loaded from the plugins folder.</summary>
    public const string ProceduresDirectoryVariable = "FISHBONE_HALCON_PROCEDURES";

    private readonly string? _proceduresDirectory;

    // a dedicated engine so the plugin owns its procedure path and does not depend on any other
    // host having configured the engine first. created only once there is a directory to register:
    // constructing one touches the native HALCON runtime, and a host that wants the operators but
    // has no procedures should not fail to load over an engine it never uses
    private HDevEngine? _engine;

    /// <summary>
    /// Used by the folder loader, which cannot pass arguments. Reads the procedures directory from
    /// the <see cref="ProceduresDirectoryVariable"/> environment variable, and registers nothing
    /// when it is not set.
    /// </summary>
    public HalconProcedurePlugin()
        : this(Environment.GetEnvironmentVariable(ProceduresDirectoryVariable))
    {
    }

    /// <param name="proceduresDirectory">
    /// The directory containing the <c>.hdvp</c> procedures. Null or empty registers nothing.
    /// </param>
    public HalconProcedurePlugin(string? proceduresDirectory)
    {
        _proceduresDirectory = proceduresDirectory;
    }

    public void Register(PluginRegistrationContext context)
    {
        FishboneConfiguration config = context.Configuration;

        // procedure control parameters are HTuple, so the same converter the operator bridge uses is
        // required; register it defensively (idempotent if the operator plugin already ran)
        config.AddTypeConverter(
            typeof(HTuple),
            toNet: value => HalconConverters.ToHTuple(value),
            fromNet: value => HalconConverters.FromHTuple((HTuple)value));

        // no directory configured is not a failure: a host that only wants the operators gets here
        if (string.IsNullOrWhiteSpace(_proceduresDirectory))
            return;

        if (!Directory.Exists(_proceduresDirectory))
        {
            context.ReportWarning($"Procedures directory \"{_proceduresDirectory}\" does not exist; no procedures registered.");
            return;
        }

        _engine ??= new HDevEngine();
        _engine.AddProcedurePath(_proceduresDirectory);

        foreach (var hdvpPath in Directory.EnumerateFiles(_proceduresDirectory, "*.hdvp"))
        {
            // procedures are called by their exact author-given name, exactly as in HDevelop, unlike
            // operators, whose script names are snake_cased to match HALCON's operator naming
            var procedureName = Path.GetFileNameWithoutExtension(hdvpPath);

            HalconProcedureCallable callable;
            try
            {
                callable = HalconProcedureCallable.Load(procedureName, hdvpPath);
            }
            catch (Exception ex)
            {
                context.ReportWarning($"Procedure \"{procedureName}\" could not be loaded and was skipped: {ex.Message}");
                continue;
            }

            // procedures win on a name collision with an existing builtin, but the shadowing is
            // surfaced so it is never silent
            if (config.BuiltIns.ContainsKey(procedureName))
                context.ReportWarning($"Procedure \"{procedureName}\" shadows an existing builtin of the same name.");

            config.AddBuiltIn(procedureName, callable);
        }
    }
}