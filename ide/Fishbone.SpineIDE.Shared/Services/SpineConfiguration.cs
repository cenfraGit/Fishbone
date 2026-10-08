using Fishbone;

namespace SpineIDE.Services;

/// <summary>
/// The configuration a SpineIDE run uses: the plugins from the default folder, plus print, println
/// and input. Completion builds the same one, so it offers exactly what a run can call.
/// </summary>
public static class SpineConfiguration
{
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
