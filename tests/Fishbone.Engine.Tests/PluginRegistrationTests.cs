using Fishbone.Core;

namespace Fishbone.Engine.Tests;

// the two ways a plugin reaches a host: referenced (AddPlugin, compile time) and discovered
// (the folder loader, runtime). both go through AddPlugin, so what a plugin reports has to come
// out the same way either way.
public class PluginRegistrationTests
{
    // --------------------------------------------------------------------------------
    // AddPlugin
    // --------------------------------------------------------------------------------

    private sealed class RecordingPlugin : IFishbonePlugin
    {
        public bool WasRegistered { get; private set; }

        public void Register(PluginRegistrationContext context)
        {
            WasRegistered = true;
            context.Configuration.AddBuiltIn("recorded", 1);
        }
    }

    private sealed class ThrowingPlugin : IFishbonePlugin
    {
        public const string FailureText = "this plugin cannot be used";

        public void Register(PluginRegistrationContext context) =>
            throw new InvalidOperationException(FailureText);
    }

    private sealed class WarningPlugin(params string[] warnings) : IFishbonePlugin
    {
        public void Register(PluginRegistrationContext context)
        {
            foreach (string warning in warnings)
                context.ReportWarning(warning);
        }
    }

    [Fact]
    public void AddPlugin_RunsThePluginAndKeepsWhatItRegistered()
    {
        var plugin = new RecordingPlugin();
        var config = new FishboneConfiguration();

        config.AddPlugin(plugin);

        Assert.True(plugin.WasRegistered);
        Assert.Equal(1, config.BuiltIns["recorded"]);
    }

    [Fact]
    public void AddPlugin_ReturnsTheSameConfigurationSoItChains()
    {
        var config = new FishboneConfiguration();

        var returned = config.AddPlugin(new RecordingPlugin());

        Assert.Same(config, returned);
    }

    [Fact]
    public void AddPlugin_ReportsWarningsToTheSinkInOrder()
    {
        var collected = new List<FishboneDiagnostic>();

        new FishboneConfiguration().AddPlugin(new WarningPlugin("first", "second"), collected.Add);

        Assert.Equal(2, collected.Count);
        Assert.EndsWith("first", collected[0].Message);
        Assert.EndsWith("second", collected[1].Message);
    }

    [Fact]
    public void AddPlugin_WarningsAreConfigurationStageWarningsWithNoLocation()
    {
        var collected = new List<FishboneDiagnostic>();

        new FishboneConfiguration().AddPlugin(new WarningPlugin("careful"), collected.Add);

        FishboneDiagnostic diagnostic = Assert.Single(collected);
        Assert.Equal(DiagnosticStage.Configuration, diagnostic.Stage);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        // a configuration problem has no place in the script to point at
        Assert.False(diagnostic.Span.IsKnown);
    }

    [Fact]
    public void AddPlugin_AttributesAWarningToThePluginThatRaisedIt()
    {
        // two plugins reporting the same words have to be tellable apart in a host's error list
        var collected = new List<FishboneDiagnostic>();

        new FishboneConfiguration().AddPlugin(new WarningPlugin("same words"), collected.Add);

        Assert.Contains(nameof(WarningPlugin), Assert.Single(collected).Message);
    }

    [Fact]
    public void AddPlugin_WithoutASink_StillRegisters()
    {
        // dropping warnings is the caller's choice, and must not cost the registration
        var config = new FishboneConfiguration();

        config.AddPlugin(new WarningPlugin("ignored me"));

        Assert.DoesNotContain("ignored me", string.Join(" ", config.BuiltIns.Keys));
    }

    [Fact]
    public void AddPlugin_APluginThatThrows_LetsItReachTheCaller()
    {
        // a direct caller asked for this plugin by name, so a failure is theirs to see. only the
        // folder loader, which registers plugins nobody explicitly asked for, swallows it
        var exception = Assert.Throws<InvalidOperationException>(
            () => new FishboneConfiguration().AddPlugin(new ThrowingPlugin()));

        Assert.Equal(ThrowingPlugin.FailureText, exception.Message);
    }

    [Fact]
    public void AddPlugin_NullPlugin_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FishboneConfiguration().AddPlugin(null!));

    [Fact]
    public void AddPlugins_RegistersEveryOne()
    {
        var config = new FishboneConfiguration();

        config.AddPlugins(new RecordingPlugin(), new WarningPlugin("noted"));

        Assert.True(config.BuiltIns.ContainsKey("recorded"));
    }

    // --------------------------------------------------------------------------------
    // PluginRegistrationContext
    // --------------------------------------------------------------------------------

    [Fact]
    public void Context_WithNoPluginName_LeavesTheWarningUnprefixed()
    {
        var context = new PluginRegistrationContext(new FishboneConfiguration());

        context.ReportWarning("bare");

        Assert.Equal("bare", Assert.Single(context.Diagnostics).Message);
    }

    [Fact]
    public void Context_CollectsNothingUntilSomethingIsReported() =>
        Assert.Empty(new PluginRegistrationContext(new FishboneConfiguration()).Diagnostics);

    [Fact]
    public void Context_NullConfiguration_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new PluginRegistrationContext(null!));
}