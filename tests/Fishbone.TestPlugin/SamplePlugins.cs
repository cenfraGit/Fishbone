// --------------------------------------------------------------------------------
// SamplePlugins.cs
//
// a real plugin assembly for the loader tests to discover.
//
// the loader finds plugins by reflecting over dlls in a folder, so testing it needs an
// actual assembly on disk that references the engine. faking that with a hand-written
// file only ever exercises the failure path. this project exists to be copied into a
// temp plugins directory and loaded for real.
//
// not a test project: it holds no tests and is referenced only so its dll lands in the
// test output where a test can find and copy it.
// --------------------------------------------------------------------------------

using Fishbone.Engine;

namespace Fishbone.TestPlugin;

/// <summary>Registers a builtin and reports a warning, which is the ordinary case.</summary>
public sealed class SamplePlugin : IFishbonePlugin
{
    public const string BuiltInName = "sample_plugin_marker";
    public const int BuiltInValue = 42;
    public const string WarningText = "sample plugin reported this";

    public void Register(PluginRegistrationContext context)
    {
        context.Configuration.AddBuiltIn(BuiltInName, BuiltInValue);
        context.ReportWarning(WarningText);
    }
}

/// <summary>
/// Throws while registering, which is how a plugin says it cannot be used at all. The loader is
/// expected to report it and carry on with the others.
/// </summary>
public sealed class FailingPlugin : IFishbonePlugin
{
    public const string FailureText = "sample plugin failed on purpose";

    public void Register(PluginRegistrationContext context) =>
        throw new InvalidOperationException(FailureText);
}

/// <summary>
/// Needs a constructor argument, so the folder loader cannot build it and must skip it without
/// complaining. A plugin shaped like this is meant to be constructed by a host that knows what to
/// pass.
/// </summary>
public sealed class NeedsHostPlugin : IFishbonePlugin
{
    public const string BuiltInName = "needs_host_marker";

    private readonly string _setting;

    public NeedsHostPlugin(string setting) => _setting = setting;

    public void Register(PluginRegistrationContext context) =>
        context.Configuration.AddBuiltIn(BuiltInName, _setting);
}