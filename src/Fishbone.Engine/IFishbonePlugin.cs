// --------------------------------------------------------------------------------
// IFishbonePlugin.cs
//
// plugins contribute to a FishboneConfiguration: builtins, preseeded values, and type
// converters for .NET types the generic interop path cannot handle on its own.
//
// there are two ways a plugin reaches a host, and they differ only in discovery:
//
// - referenced. the host installs the plugin's nuget package and calls
//   config.AddPlugin(new SomePlugin()). the host decides what is in scope at compile
//   time, a missing plugin is a compile error, and no reflection is involved. this is
//   the normal case for an app embedding fishbone.
//
// - discovered. the plugin dll sits in a plugins folder and is found by reflection at
//   startup. the end user decides what is in scope at runtime, without recompiling
//   anything. this is for standalone hosts, and requires a parameterless constructor.
//
// same contract either way.
// --------------------------------------------------------------------------------

namespace Fishbone.Engine;

public interface IFishbonePlugin
{
    /// <summary>
    /// Contributes to <see cref="PluginRegistrationContext.Configuration"/>. Throw to signal that
    /// the plugin cannot be used at all; use <see cref="PluginRegistrationContext.ReportWarning"/>
    /// for anything the host should see that did not stop registration.
    /// </summary>
    void Register(PluginRegistrationContext context);
}