using Fishbone.Core;
using Fishbone.Engine;

namespace Fishbone.Hosting.Tests;

// discovery of a real plugin assembly from disk.
//
// the fixture is built by tests/Fishbone.TestPlugin but deliberately not referenced by this
// project. that matters: Assembly.LoadFrom matches on assembly identity, so if the test assembly
// bound to the fixture at compile time the runtime would already have it loaded and LoadFrom would
// hand back that copy rather than reading the file. the tests would then pass without the loader
// ever having loaded anything.
//
// the price is that the fixture's names are string literals here instead of nameof. they are the
// fixture's observable contract, and its only consumer is this file.

/// <summary>
/// One plugins folder shared by the whole class. Per-test folders do not work: the loader reaches
/// the plugin through <c>Assembly.LoadFrom</c>, which holds the file open for the life of the
/// process, so every test would leave an undeletable directory behind.
/// </summary>
public sealed class PluginFixtureDirectory : IDisposable
{
    public const string AssemblyFile = "Fishbone.TestPlugin.dll";

    public PluginFixtureDirectory()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "fixtures", AssemblyFile);
        if (!File.Exists(source))
            throw new FileNotFoundException(
                $"{AssemblyFile} should have been copied to the fixtures folder by the build", source);

        Root = Path.Combine(Path.GetTempPath(), $"fishbone-discovery-{Guid.NewGuid():N}");
        PluginDirectory = Path.Combine(Root, "TestPlugin");
        Directory.CreateDirectory(PluginDirectory);
        File.Copy(source, Path.Combine(PluginDirectory, AssemblyFile));
    }

    /// <summary>The plugins root, as a host would pass it to the loader.</summary>
    public string Root { get; }

    /// <summary>The single plugin folder inside it.</summary>
    public string PluginDirectory { get; }

    public void Dispose()
    {
        // best effort. once the loader has read the assembly the file stays open, so this usually
        // cannot succeed, and a temp directory is not worth failing a run over
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public class PluginDiscoveryTests(PluginFixtureDirectory fixture) : IClassFixture<PluginFixtureDirectory>
{
    // what SamplePlugin registers and reports
    private const string SamplePluginType = "SamplePlugin";
    private const string SampleBuiltIn = "sample_plugin_marker";
    private const int SampleValue = 42;
    private const string SampleWarning = "sample plugin reported this";

    // FailingPlugin throws this while registering
    private const string FailingPluginType = "FailingPlugin";
    private const string FailureText = "sample plugin failed on purpose";

    // NeedsHostPlugin has no parameterless constructor
    private const string NeedsHostPluginType = "NeedsHostPlugin";
    private const string NeedsHostBuiltIn = "needs_host_marker";

    [Fact]
    public void Load_DiscoversAPluginAndRegistersWhatItContributes()
    {
        var config = new FishboneConfiguration();

        PluginLoadResult result = FishbonePluginLoader.Load(fixture.Root, config);

        Assert.Contains(result.Loaded, entry => entry.Contains(SamplePluginType));
        Assert.Equal(SampleValue, config.BuiltIns[SampleBuiltIn]);
    }

    [Fact]
    public void Load_CarriesAPluginsOwnWarningOut()
    {
        // this is the whole point of the registration context: before it, a discovered plugin had
        // no way to tell the host anything short of throwing
        PluginLoadResult result = FishbonePluginLoader.Load(fixture.Root, new FishboneConfiguration());

        FishboneDiagnostic warning = Assert.Single(
            result.Diagnostics,
            d => d.Message.Contains(SampleWarning));

        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(DiagnosticStage.Configuration, warning.Stage);
        Assert.Contains(SamplePluginType, warning.Message);
    }

    [Fact]
    public void Load_APluginThatThrows_IsReportedAndDoesNotStopTheOthers()
    {
        var config = new FishboneConfiguration();

        PluginLoadResult result = FishbonePluginLoader.Load(fixture.Root, config);

        FishboneDiagnostic failure = Assert.Single(
            result.Diagnostics,
            d => d.Message.Contains(FailureText));
        Assert.Equal(DiagnosticSeverity.Error, failure.Severity);
        Assert.Contains(FailingPluginType, failure.Message);

        // the good plugin in the same assembly still registered
        Assert.True(config.BuiltIns.ContainsKey(SampleBuiltIn));
    }

    [Fact]
    public void Load_APluginNeedingConstructorArguments_IsSkippedQuietly()
    {
        // that shape is meant for a host to construct, so passing it over is correct rather than a
        // failure worth reporting
        var config = new FishboneConfiguration();

        PluginLoadResult result = FishbonePluginLoader.Load(fixture.Root, config);

        Assert.False(config.BuiltIns.ContainsKey(NeedsHostBuiltIn));
        Assert.DoesNotContain(result.Loaded, entry => entry.Contains(NeedsHostPluginType));
        Assert.DoesNotContain(result.Diagnostics, d => d.Message.Contains(NeedsHostPluginType));
    }

    [Fact]
    public void Load_ResolvesThePluginsEngineReferenceWithoutACopyBesideIt()
    {
        // the plugin folder holds only the plugin dll, so loading it means resolving
        // Fishbone.Engine from the host. a plugin that shipped its own copy would bind
        // IFishbonePlugin to a different type and never be recognised, which is why the plugin
        // projects keep the engine out of their output
        Assert.Single(Directory.GetFiles(fixture.PluginDirectory, "*.dll"));

        PluginLoadResult result = FishbonePluginLoader.Load(fixture.Root, new FishboneConfiguration());

        Assert.Contains(result.Loaded, entry => entry.Contains(SamplePluginType));
    }

    [Fact]
    public void Load_TwiceOverTheSameFolder_RegistersTheSameThingAgain()
    {
        // hosts call this per run, and SpineIDE calls it per script execution, so a second pass
        // over an already-loaded assembly has to behave like the first
        var first = new FishboneConfiguration();
        var second = new FishboneConfiguration();

        FishbonePluginLoader.Load(fixture.Root, first);
        FishbonePluginLoader.Load(fixture.Root, second);

        Assert.Equal(first.BuiltIns[SampleBuiltIn], second.BuiltIns[SampleBuiltIn]);
    }
}
