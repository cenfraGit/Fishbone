using System.Reflection;
using Fishbone.Engine;

namespace Fishbone.Hosting.Tests;

// a discovered plugin was compiled against some version of the engine, and nothing stops someone
// dropping one built against a different major into the plugins folder. without this check that
// surfaces as a TypeLoadException naming an internal type, which tells whoever installed it
// nothing at all.
public class EngineVersionCompatibilityTests
{
    private static AssemblyName Named(string version) =>
        new("Fishbone.Engine") { Version = Version.Parse(version) };

    [Theory]
    [InlineData("1.0.0.0", "1.0.0.0")]   // identical
    [InlineData("1.0.0.0", "1.4.0.0")]   // plugin built against an older minor still loads
    [InlineData("1.9.0.0", "1.2.0.0")]   // and a newer minor is not refused either
    public void SameMajorVersion_IsCompatible(string plugin, string host) =>
        Assert.True(FishbonePluginLoader.IsEngineVersionCompatible(Named(plugin), Named(host)));

    [Theory]
    [InlineData("1.0.0.0", "2.0.0.0")]
    [InlineData("2.0.0.0", "1.0.0.0")]
    public void DifferentMajorVersion_IsNotCompatible(string plugin, string host) =>
        Assert.False(FishbonePluginLoader.IsEngineVersionCompatible(Named(plugin), Named(host)));

    [Fact]
    public void MissingVersion_IsTreatedAsCompatible()
    {
        // an unversioned build should fail on its own terms rather than be refused on a guess
        var unversioned = new AssemblyName("Fishbone.Engine");

        Assert.True(FishbonePluginLoader.IsEngineVersionCompatible(unversioned, Named("1.0.0.0")));
        Assert.True(FishbonePluginLoader.IsEngineVersionCompatible(Named("1.0.0.0"), unversioned));
    }

    [Fact]
    public void TheRealEngineIsCompatibleWithItself()
    {
        // guards against the check rejecting every plugin, which a unit test on the pure function
        // cannot notice
        AssemblyName engine = typeof(IFishbonePlugin).Assembly.GetName();

        Assert.True(FishbonePluginLoader.IsEngineVersionCompatible(engine, engine));
    }
}
