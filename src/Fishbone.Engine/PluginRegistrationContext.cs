// --------------------------------------------------------------------------------
// PluginRegistrationContext.cs
//
// what a plugin is handed when it registers.
//
// this used to be the FishboneConfiguration itself, which left a plugin no way to say
// "i registered, but something was off". HalconProcedurePlugin worked around that by
// taking an Action<string> in its constructor, which is the contract's gap showing up
// in a plugin's own signature.
//
// a context object rather than extra parameters on Register: adding a property here is
// not a breaking change, adding a parameter to an interface method is. settling this
// before 1.0 is only worth it if it does not have to be settled again.
// --------------------------------------------------------------------------------

using Fishbone.Core;

namespace Fishbone.Engine;

/// <summary>
/// Handed to <see cref="IFishbonePlugin.Register"/>. Carries the configuration the plugin
/// contributes to, plus a channel for anything the host should know about that did not stop
/// registration.
/// </summary>
public sealed class PluginRegistrationContext
{
    private readonly List<FishboneDiagnostic> _diagnostics = [];

    /// <param name="configuration">The configuration the plugin registers into.</param>
    /// <param name="pluginName">
    /// Name used to attribute the plugin's warnings. Pass null to leave them unattributed.
    /// </param>
    public PluginRegistrationContext(FishboneConfiguration configuration, string? pluginName = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Configuration = configuration;
        PluginName = pluginName;
    }

    /// <summary>Where the plugin registers its builtins, values and type converters.</summary>
    public FishboneConfiguration Configuration { get; }

    /// <summary>The plugin's name, prefixed onto its warnings. Null when the host supplied none.</summary>
    public string? PluginName { get; }

    /// <summary>What the plugin reported while registering, in the order it was reported.</summary>
    public IReadOnlyList<FishboneDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Reports something the host should surface but which did not stop registration: a procedure
    /// that could not be read, a name that shadows an existing builtin. A plugin that cannot
    /// register at all should throw instead, which the folder loader already turns into a
    /// diagnostic of its own.
    /// </summary>
    public void ReportWarning(string message)
    {
        _diagnostics.Add(FishboneDiagnostics.Configuration(
            PluginName is null ? message : $"{PluginName}: {message}",
            DiagnosticSeverity.Warning));
    }
}