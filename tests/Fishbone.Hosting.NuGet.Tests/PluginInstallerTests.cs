using NuGet.Versioning;

namespace Fishbone.Hosting.NuGet.Tests;

// the parts of the installer that touch a disk but not a feed. downloading is covered by
// installing a real package end to end, which needs a feed and belongs outside a unit test.
public class PluginInstallerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"fishbone-installer-{Guid.NewGuid():N}");

    private PluginInstaller Installer() => new(_root);

    private string CreatePlugin(string packageId, string? version, params string[] files)
    {
        string directory = Path.Combine(_root, packageId);
        Directory.CreateDirectory(directory);

        foreach (string file in files)
            File.WriteAllText(Path.Combine(directory, file), "x");

        if (version is not null)
            File.WriteAllText(Path.Combine(directory, ".fishbone-plugin-version"), version);

        return directory;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // --------------------------------------------------------------------------------
    // list
    // --------------------------------------------------------------------------------

    [Fact]
    public void List_AMissingPluginsFolder_IsEmptyRatherThanAnError() =>
        Assert.Empty(new PluginInstaller(Path.Combine(_root, "nope")).List());

    [Fact]
    public void List_ReportsTheVersionThatWasInstalled()
    {
        CreatePlugin("Contoso.Plugin", "2.1.0", "Contoso.Plugin.dll");

        InstalledPlugin plugin = Assert.Single(Installer().List());

        Assert.Equal("Contoso.Plugin", plugin.PackageId);
        Assert.Equal("2.1.0", plugin.Version);
        Assert.Equal(["Contoso.Plugin.dll"], plugin.Files);
    }

    [Fact]
    public void List_AFolderCopiedInByHand_IsStillListed()
    {
        // it has no version marker, but the loader will happily run it, so hiding it from the
        // listing would leave somebody unable to explain where a builtin came from
        CreatePlugin("HandCopied", version: null, "HandCopied.dll");

        InstalledPlugin plugin = Assert.Single(Installer().List());

        Assert.Equal("HandCopied", plugin.PackageId);
        Assert.Equal(string.Empty, plugin.Version);
    }

    [Fact]
    public void List_DoesNotShowTheVersionMarkerAsAFile()
    {
        // it is bookkeeping, not part of the plugin
        CreatePlugin("Contoso.Plugin", "1.0.0", "Contoso.Plugin.dll", "Dep.dll");

        InstalledPlugin plugin = Assert.Single(Installer().List());

        Assert.Equal(["Contoso.Plugin.dll", "Dep.dll"], plugin.Files);
    }

    [Fact]
    public void List_AnEmptyFolder_IsListedWithNoFiles()
    {
        // exactly what a leftover from an older layout looks like, and worth being visible
        CreatePlugin("Leftover", version: null);

        InstalledPlugin plugin = Assert.Single(Installer().List());

        Assert.Empty(plugin.Files);
    }

    // --------------------------------------------------------------------------------
    // remove
    // --------------------------------------------------------------------------------

    [Fact]
    public void Remove_DeletesTheFolder()
    {
        string directory = CreatePlugin("Contoso.Plugin", "1.0.0", "Contoso.Plugin.dll");

        PluginRemoveResult result = Installer().Remove("Contoso.Plugin");

        Assert.True(result.Removed);
        Assert.Empty(result.Diagnostics);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Remove_SomethingNotInstalled_ReportsRatherThanThrows()
    {
        PluginRemoveResult result = Installer().Remove("Nope.NotHere");

        Assert.False(result.Removed);
        Assert.Contains("not installed", Assert.Single(result.Diagnostics).Message);
    }

    [Fact]
    public void Remove_LeavesOtherPluginsAlone()
    {
        CreatePlugin("Keep", "1.0.0", "Keep.dll");
        CreatePlugin("Drop", "1.0.0", "Drop.dll");

        Installer().Remove("Drop");

        Assert.Equal("Keep", Assert.Single(Installer().List()).PackageId);
    }

    [Fact]
    public void Remove_AFolderHeldOpen_SaysToCloseTheHost()
    {
        // the case that actually happens: a running host reached the plugin through
        // Assembly.LoadFrom, which holds the file open until the process exits
        string directory = CreatePlugin("Locked", "1.0.0", "Locked.dll");
        using FileStream hold = File.Open(
            Path.Combine(directory, "Locked.dll"), FileMode.Open, FileAccess.Read, FileShare.None);

        PluginRemoveResult result = Installer().Remove("Locked");

        Assert.False(result.Removed);
        Assert.Contains("close it first", Assert.Single(result.Diagnostics).Message);
    }

    // --------------------------------------------------------------------------------
    // pieces of resolution that are worth pinning on their own
    // --------------------------------------------------------------------------------

    [Theory]
    [InlineData("Fishbone")]
    [InlineData("fishbone")]
    [InlineData("Fishbone.Core")]
    [InlineData("Fishbone.Engine")]
    [InlineData("Fishbone.Hosting")]
    public void TheEnginesOwnPackages_AreNeverFollowedAsDependencies(string packageId) =>
        // every plugin depends on Fishbone. downloading it would fetch the host's own
        // assemblies and then discard every file in the package
        Assert.True(PluginInstaller.IsHostSuppliedPackage(packageId));

    [Theory]
    [InlineData("Fishbone.Plugins.OpenCv")]
    [InlineData("OpenCvSharp4")]
    [InlineData("Contoso.Fishbone.Plugins.Thing")]
    public void ARealDependency_IsFollowed(string packageId) =>
        Assert.False(PluginInstaller.IsHostSuppliedPackage(packageId));

    [Theory]
    [InlineData("[1.2.3]", "1.2.3")]        // exact
    [InlineData("1.2.3", "1.2.3")]          // minimum, which is what a nuspec normally carries
    [InlineData("[1.2.3, 2.0.0)", "1.2.3")]
    public void ADependencyRange_ResolvesToItsLowestSatisfyingVersion(string range, string expected) =>
        // matching what nuget does, so an install pulls the same versions a project reference would
        Assert.Equal(expected,
            PluginInstaller.LowestSatisfying(VersionRange.Parse(range)).ToNormalizedString());

    [Fact]
    public void AnUpperBoundOnlyRange_StillResolvesToSomething()
    {
        // no minimum is unusual but legal, and returning null here would abort an install that
        // could have worked
        Assert.NotNull(PluginInstaller.LowestSatisfying(VersionRange.Parse("(, 2.0.0)")));
    }

    // --------------------------------------------------------------------------------
    // platform identifiers
    // --------------------------------------------------------------------------------

    [Fact]
    public void TheCurrentPlatform_IsAWellFormedIdentifier()
    {
        string current = RuntimeIdentifiers.Current;

        Assert.False(string.IsNullOrWhiteSpace(current));
        Assert.DoesNotContain(' ', current);
        Assert.Equal(current.ToLowerInvariant(), current);
    }

    [Fact]
    public void TheFallbackChain_GoesFromSpecificToGeneral() =>
        Assert.Equal(["win-x64", "win", "any"], RuntimeIdentifiers.FallbackChain("win-x64"));

    [Fact]
    public void TheFallbackChain_NeverRepeatsAny() =>
        Assert.Equal(["any"], RuntimeIdentifiers.FallbackChain("any"));

    [Fact]
    public void TheFallbackChain_HandlesAPlatformWithNoArchitecture() =>
        Assert.Equal(["win", "any"], RuntimeIdentifiers.FallbackChain("win"));

    [Fact]
    public void TheFallbackChain_IsNeverEmpty() =>
        Assert.NotEmpty(RuntimeIdentifiers.FallbackChain(string.Empty));
}
