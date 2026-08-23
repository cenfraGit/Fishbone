using Fishbone.Core;
using Fishbone.Engine;

namespace Fishbone.Hosting.Tests;

// loading is best effort, so the failures have to come back as data rather than be printed. these
// moved here with the loader itself; they used to sit in the engine's diagnostic tests.
public class PluginLoadFailureTests
{
    [Fact]
    public void PluginLoad_ReportsFailuresAsDiagnosticsInsteadOfPrinting()
    {
        // a gui host has no console, so the loader must hand its failures back.
        // a dll that is not a managed assembly is the cheapest way to force one
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(directory, "broken"));
        File.WriteAllText(Path.Combine(directory, "broken", "notreal.dll"), "this is not an assembly");

        try
        {
            var result = FishbonePluginLoader.Load(directory, new FishboneConfiguration());

            Assert.Empty(result.Loaded);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(DiagnosticStage.Configuration, diagnostic.Stage);
            Assert.Contains("Failed to load plugin assembly", diagnostic.Message);
            Assert.False(diagnostic.Span.IsKnown);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PluginLoad_ANativeLibraryBesideAPlugin_IsNotReportedAsAFailure()
    {
        // a plugin folder legitimately holds native libraries: installing the OpenCV plugin puts
        // OpenCvSharpExtern.dll next to its assembly. those are not assemblies and reporting each
        // one as a failed plugin buried the real diagnostics.
        //
        // any native library will do as a stand-in, and every machine running this has one
        string source = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!, NativeLibraryName);
        Assert.True(File.Exists(source), $"expected a native library at {source}");

        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(directory, "withnative"));
        File.Copy(source, Path.Combine(directory, "withnative", "somenative.dll"));

        try
        {
            var result = FishbonePluginLoader.Load(directory, new FishboneConfiguration());

            Assert.Empty(result.Loaded);
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // the runtime's own native host, which sits beside System.Private.CoreLib on every platform
    private static string NativeLibraryName =>
        OperatingSystem.IsWindows() ? "hostpolicy.dll"
        : OperatingSystem.IsMacOS() ? "libhostpolicy.dylib"
        : "libhostpolicy.so";

    [Fact]
    public void PluginLoad_MissingDirectoryReportsNothing()
    {
        var result = FishbonePluginLoader.Load(
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), new FishboneConfiguration());

        Assert.Empty(result.Loaded);
        Assert.Empty(result.Diagnostics);
    }
}
