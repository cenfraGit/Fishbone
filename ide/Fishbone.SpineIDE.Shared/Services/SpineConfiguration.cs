using Fishbone;

namespace SpineIDE.Services;

/// <summary>
/// The configuration a SpineIDE run uses: the plugins from the default folder, plus print, println
/// and input. The editor describes the same one, so it offers exactly what a run can call.
/// </summary>
public static class SpineConfiguration
{
    private static readonly Lazy<FishboneDescription?> LazyDescription = new(() =>
    {
        try
        {
            return Create(_ => { }, _ => { }, () => string.Empty).Describe();
        }
        catch
        {
            // a broken plugin shouldn't take the editor down. it just has nothing to suggest
            return null;
        }
    }, isThreadSafe: true);

    /// <summary>What a run's configuration puts into scripts, for completion and the analyzer. The first use loads plugins, so warm it off the ui thread.</summary>
    public static FishboneDescription? Description => LazyDescription.Value;

    public static FishboneConfiguration Create(Action<object?> print, Action<object?> println, Func<string> input)
    {
        var configuration = new FishboneConfiguration();
        FishbonePluginLoader.LoadPlugins(FishbonePluginLoader.DefaultPluginsDirectory, configuration);
        configuration.AddBuiltIn("print", print);
        configuration.AddBuiltIn("println", println);
        configuration.AddBuiltIn("input", input);
        return configuration;
    }
}
