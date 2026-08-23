using NuGet.Frameworks;

namespace Fishbone.Hosting.NuGet.Tests;

// choosing files out of a package layout. the entry lists here are the real ones from
// OpenCvSharp4 and OpenCvSharp4.runtime.win, read out of the nuget cache, because the shapes
// that break this are the ones real packages actually use.
public class PluginPackageLayoutTests
{
    private static readonly NuGetFramework Net8 = NuGetFramework.Parse("net8.0");

    private static IReadOnlyList<PluginFile> Files(
        IEnumerable<string> entries, string rid = "win-x64", NuGetFramework? framework = null) =>
        PluginPackageLayout.Select(entries, framework ?? Net8, rid).Files;

    private static string[] Names(
        IEnumerable<string> entries, string rid = "win-x64", NuGetFramework? framework = null) =>
        Files(entries, rid, framework)
            .Select(file => file.FileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static EmptyReason Reason(
        IEnumerable<string> entries, string rid = "win-x64", NuGetFramework? framework = null) =>
        PluginPackageLayout.Select(entries, framework ?? Net8, rid).EmptyReason;

    // --------------------------------------------------------------------------------
    // framework selection
    // --------------------------------------------------------------------------------

    // OpenCvSharp4 4.10, verbatim
    private static readonly string[] OpenCvSharp =
    [
        ".signature.p7s",
        "lib/net48/OpenCvSharp.dll",
        "lib/net48/OpenCvSharp.xml",
        "lib/net6.0/OpenCvSharp.dll",
        "lib/net6.0/OpenCvSharp.xml",
        "lib/netstandard2.0/OpenCvSharp.dll",
        "lib/netstandard2.0/OpenCvSharp.xml",
        "lib/netstandard2.1/OpenCvSharp.dll",
        "lib/netstandard2.1/OpenCvSharp.xml",
        "opencvsharp4.nuspec",
    ];

    [Fact]
    public void FourFrameworks_TakesTheNearestOneOnly()
    {
        // net6.0 is nearest for a net8.0 host. taking more than one folder would put two
        // OpenCvSharp.dll files in the same directory
        IReadOnlyList<PluginFile> files = Files(OpenCvSharp);

        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.StartsWith("lib/net6.0/", file.PackageEntry));
    }

    [Fact]
    public void FrameworkFolders_AreNotJustPickedAlphabetically()
    {
        // net48 sorts first and netstandard2.1 sorts last, so a naive implementation lands on
        // one of those. neither is the right answer for net8.0
        IReadOnlyList<PluginFile> files = Files(OpenCvSharp);

        Assert.DoesNotContain(files, f => f.PackageEntry.Contains("net48"));
        Assert.DoesNotContain(files, f => f.PackageEntry.Contains("netstandard"));
    }

    [Fact]
    public void ANetFrameworkOnlyPackage_YieldsNothingAndSaysItIsIncompatible()
    {
        // installing net48 assemblies into a net8.0 host would fail at load time, so choosing
        // nothing is right, and this is the one empty case worth reporting
        string[] entries = ["lib/net48/Legacy.dll"];

        Assert.Empty(Files(entries));
        Assert.Equal(EmptyReason.Incompatible, Reason(entries));
    }

    [Fact]
    public void NuspecSignatureAndOtherPackageMetadata_AreNeverInstalled() =>
        Assert.DoesNotContain(Names(OpenCvSharp), name =>
            name.EndsWith(".nuspec", StringComparison.Ordinal) || name.EndsWith(".p7s", StringComparison.Ordinal));

    [Fact]
    public void XmlDocumentation_IsInstalledBesideItsAssembly() =>
        Assert.Equal(["OpenCvSharp.dll", "OpenCvSharp.xml"], Names(OpenCvSharp));

    // --------------------------------------------------------------------------------
    // native assets
    // --------------------------------------------------------------------------------

    // OpenCvSharp4.runtime.win 4.10, verbatim. note the build/ folder: it holds msbuild props
    // that mean nothing to a plugin folder
    private static readonly string[] OpenCvNative =
    [
        "build/net/OpenCvSharp4.runtime.win.props",
        "runtimes/win-x64/native/OpenCvSharpExtern.dll",
        "runtimes/win-x64/native/opencv_videoio_ffmpeg4100_64.dll",
        "runtimes/win-x86/native/OpenCvSharpExtern.dll",
        "runtimes/win-x86/native/opencv_videoio_ffmpeg4100.dll",
        "opencvsharp4.runtime.win.nuspec",
    ];

    [Fact]
    public void NativeAssets_AreTakenForTheHostArchitectureOnly()
    {
        // both architectures ship the same file name, so taking both would overwrite one with
        // the other and produce a plugin that fails in a way nobody can read
        Assert.All(Files(OpenCvNative), file => Assert.Contains("win-x64", file.PackageEntry));
        Assert.Equal(["OpenCvSharpExtern.dll", "opencv_videoio_ffmpeg4100_64.dll"], Names(OpenCvNative));
    }

    [Fact]
    public void NativeAssets_FollowTheRequestedArchitecture() =>
        Assert.Equal(["OpenCvSharpExtern.dll", "opencv_videoio_ffmpeg4100.dll"],
                     Names(OpenCvNative, rid: "win-x86"));

    [Fact]
    public void NativeAssets_AreFlattenedNotLeftUnderRuntimes()
    {
        // the runtimes/ tree only means anything with a deps.json describing it, which an
        // installed plugin folder cannot produce. flattened, default native probing finds them
        Assert.All(Files(OpenCvNative), file => Assert.DoesNotContain('/', file.FileName));
    }

    [Fact]
    public void MsbuildAssetsInBuild_AreNotInstalled() =>
        Assert.DoesNotContain(Names(OpenCvNative), name => name.EndsWith(".props", StringComparison.Ordinal));

    [Fact]
    public void AnUnknownPlatform_FallsBackToThePlatformFolder()
    {
        string[] entries = ["runtimes/win/native/Shared.dll"];

        // no win-arm64 folder exists, so the bare platform folder is used
        Assert.Equal(["Shared.dll"], Names(entries, rid: "win-arm64"));
    }

    [Fact]
    public void ANativePackageForAnotherPlatform_IsNotWorthAWarning()
    {
        // OpenCvSharp4.official.runtime.linux-x64 is pulled in as a dependency on windows too.
        // it having nothing here is expected, and warning about it put noise on every install
        string[] entries = ["runtimes/linux-x64/native/libOpenCvSharpExtern.so"];

        Assert.Empty(Files(entries));
        Assert.Equal(EmptyReason.NothingToInstall, Reason(entries));
    }

    // --------------------------------------------------------------------------------
    // rid-specific managed assemblies
    // --------------------------------------------------------------------------------

    [Fact]
    public void ARidSpecificAssembly_WinsOverThePortableOne()
    {
        // a package shipping both means the portable one is a fallback for platforms without a
        // specific build. installing both would leave which one loads to directory ordering
        string[] entries =
        [
            "lib/net8.0/Platform.dll",
            "runtimes/win-x64/lib/net8.0/Platform.dll",
        ];

        PluginFile file = Assert.Single(Files(entries));
        Assert.Equal("runtimes/win-x64/lib/net8.0/Platform.dll", file.PackageEntry);
    }

    [Fact]
    public void ARidSpecificAssemblyForAnotherPlatform_LeavesThePortableOne()
    {
        string[] entries =
        [
            "lib/net8.0/Platform.dll",
            "runtimes/linux-x64/lib/net8.0/Platform.dll",
        ];

        PluginFile file = Assert.Single(Files(entries));
        Assert.Equal("lib/net8.0/Platform.dll", file.PackageEntry);
    }

    // --------------------------------------------------------------------------------
    // the engine's own assemblies
    // --------------------------------------------------------------------------------

    [Theory]
    [InlineData("Fishbone.Core.dll")]
    [InlineData("Fishbone.Engine.dll")]
    [InlineData("Fishbone.Interpreter.dll")]
    [InlineData("Fishbone.Parser.dll")]
    [InlineData("Fishbone.Debugging.dll")]
    [InlineData("Fishbone.Hosting.dll")]
    public void HostSuppliedAssemblies_AreNeverInstalled(string fileName)
    {
        // a second copy would bind IFishbonePlugin to a different type than the host checks
        // against, and the plugin would silently never be recognised
        Assert.True(PluginPackageLayout.IsHostSuppliedAssembly(fileName));
        Assert.Empty(Files([$"lib/net8.0/{fileName}"]));
    }

    [Theory]
    [InlineData("Fishbone.Plugins.Halcon.dll")]
    [InlineData("Fishbone.Plugins.OpenCv.dll")]
    [InlineData("Contoso.Fishbone.Plugins.Thing.dll")]
    public void APluginsOwnAssembly_IsInstalledEvenThoughItIsNamedFishbone(string fileName)
    {
        // the reason the exclusion is an explicit list and not a "Fishbone.*" prefix test
        Assert.False(PluginPackageLayout.IsHostSuppliedAssembly(fileName));
        Assert.Equal([fileName], Names([$"lib/net8.0/{fileName}"]));
    }

    // --------------------------------------------------------------------------------
    // shapes that should simply be ignored
    // --------------------------------------------------------------------------------

    [Theory]
    [InlineData("ref/net8.0/Thing.dll")]              // reference assembly, no implementation
    [InlineData("build/net8.0/Thing.props")]
    [InlineData("buildTransitive/net8.0/Thing.targets")]
    [InlineData("tools/net8.0/any/Thing.dll")]
    [InlineData("contentFiles/any/any/readme.txt")]
    [InlineData("analyzers/dotnet/cs/Thing.dll")]
    [InlineData("lib/net8.0/cs/Thing.resources.dll")]  // satellite resource, in a subfolder
    public void NonRuntimeEntries_AreIgnored(string entry) =>
        Assert.Empty(Files([entry]));

    [Fact]
    public void APlaceholderFrameworkFolder_MeansTheFrameworkProvidesIt()
    {
        // System.Memory ships lib/netcoreapp2.1/_._ , nuget's way of saying a modern runtime
        // already has this. treating that as a problem warned on every successful install
        string[] entries = ["lib/netstandard2.0/System.Memory.dll", "lib/netcoreapp2.1/_._"];

        Assert.Empty(Files(entries));
        Assert.Equal(EmptyReason.ProvidedByFramework, Reason(entries));
    }

    [Fact]
    public void BackslashSeparators_AreHandled()
    {
        // some tooling writes package entries with windows separators
        Assert.Equal(["Thing.dll"], Names([@"lib\net8.0\Thing.dll"]));
    }

    [Fact]
    public void AnEmptyPackage_YieldsNothingAndNothingToSay()
    {
        Assert.Empty(Files([]));
        Assert.Equal(EmptyReason.NothingToInstall, Reason([]));
    }

    [Fact]
    public void AFullSelection_ReportsNoEmptyReason() =>
        Assert.Equal(EmptyReason.NotEmpty, Reason(OpenCvSharp));

    [Fact]
    public void NoFileNameIsEverProducedTwice()
    {
        // whatever the layout, two files with one name in one folder is not installable
        string[] entries =
        [
            "lib/net6.0/Thing.dll",
            "lib/net8.0/Thing.dll",
            "runtimes/win-x64/lib/net8.0/Thing.dll",
            "runtimes/win-x64/native/Thing.dll",
        ];

        string[] names = Names(entries);

        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
