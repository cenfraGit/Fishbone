using System.Diagnostics;
using Fishbone;
using Fishbone.Debugging;

namespace Fishbone.DebugAdapter.Tests;

/// <summary>
/// A test that needs a person, so it only runs under a debugger: in Visual Studio's Test Explorer,
/// right-click it and pick Debug. Run All, dotnet test and CI skip it, unless FISHBONE_MANUAL_TESTS
/// is set to 1.
/// </summary>
public sealed class ManualFactAttribute : FactAttribute
{
    public ManualFactAttribute()
    {
        if (!Debugger.IsAttached && Environment.GetEnvironmentVariable("FISHBONE_MANUAL_TESTS") != "1")
            Skip = "manual: debug this test from Test Explorer to open SpineIDE attached to it";
    }
}

/// <summary>
/// Opens the SpineIDE built from this repo, attached to a host the way <c>RunDebuggableAsync</c>
/// does for an app. The IDE stops on the first line. Step around, check images into the preview,
/// try watches, then continue to the end, where it stops once more on the final values, and
/// continue again to finish. The host loads the plugins installed in ~/.fishbone/plugins, like
/// SpineIDE's own debug host does. A SpineIDE an earlier test opened is reused while it's idle.
/// </summary>
[Collection("DebugServer")]
public class SpineIdeAttachTests
{
    private const string Script = """
        let image = picture;
        let blobs = squares;
        let outline = ring;
        let total = 0;
        let i = 0;
        while (i < 5)
        {
            total = total + i;
            i = i + 1;
        }
        println("total is " + total.ToString());
        """;

    [ManualFact]
    public async Task RunDebuggableAsync_OpensSpineIdeAttached()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("picture", new Picture())
            .AddBuiltIn("squares", new Shapes(Squares: true))
            .AddBuiltIn("ring", new Shapes(Squares: false))
            .AddVisualizer<Picture>(_ => Gradient())
            .AddVisualizer<Shapes>(shapes => shapes.Squares ? SquareRegions() : RingContour());

        var result = await RunAttachedAsync(Script, "attach-test.fb", config);

        Assert.Equal(10, result.Environment!.GetValue("total"));
    }

    // the same window debugs both runs: the second attaches to the SpineIDE the first opened
    [ManualFact]
    public async Task RunDebuggableAsync_Twice_ReusesTheSpineIde()
    {
        for (int run = 1; run <= 2; run++)
        {
            var result = await RunAttachedAsync($"let run = {run};\nlet image = picture;\nprintln(run);", $"run-{run}.fb", new FishboneConfiguration()
                .AddBuiltIn("picture", new Picture())
                .AddVisualizer<Picture>(_ => Gradient()));
            Assert.Equal(run, result.Environment!.GetValue("run"));
        }
    }

    // the regions sample, for HALCON regions and contours in the preview, and watches like
    // count_obj(blobs, out n). it needs the HALCON plugin installed
    [ManualFact]
    public async Task RunDebuggableAsync_OpensSpineIdeOnTheHalconRegionsSample()
    {
        string sample = Path.Combine(RepositoryRoot().FullName, "samples", "halcon_regions.fb");
        var config = new FishboneConfiguration();
        string loaded = LoadInstalledPlugins(config);
        Assert.True(config.BuiltIns.ContainsKey("read_image"), $"the HALCON plugin didn't load. {loaded}");

        var result = await RunAttachedAsync(File.ReadAllText(sample), Path.GetFileName(sample), config, pluginsLoaded: true);

        Assert.True(result.Environment!.IsDefined("blobs"));
    }

    // the plugins installed on this machine, not the empty folder the other tests use. returns
    // what loaded and what failed, which the loader only writes to stderr
    private static string LoadInstalledPlugins(FishboneConfiguration config)
    {
        var errors = new StringWriter();
        TextWriter stderr = Console.Error;
        Console.SetError(errors);
        try
        {
            var loaded = FishbonePluginLoader.LoadPlugins(IsolatedPlugins.MachineDirectory, config);
            return $"from {IsolatedPlugins.MachineDirectory}, loaded: [{string.Join(", ", loaded)}], errors: [{errors.ToString().Trim()}]";
        }
        finally
        {
            Console.SetError(stderr);
        }
    }

    private static async Task<FishboneRunResult> RunAttachedAsync(string script, string name, FishboneConfiguration config, bool pluginsLoaded = false)
    {
        string ide = FindSpineIde();
        // attached, println goes to the IDE's output instead
        config.AddBuiltIn("println", new Action<object?>(Console.WriteLine));
        if (!pluginsLoaded)
            LoadInstalledPlugins(config);

        var result = await FishboneProgram.FromSourceCode(script).RunDebuggableAsync(config, new FishboneDebugOptions
        {
            OpenIde = true,
            AttachTimeout = TimeSpan.FromSeconds(30),
            SourceName = name,
            IdeLauncher = endpoint => LaunchWithInstalledPlugins(() => SpineIdeLauncher.Launch(endpoint, ide)),
        });

        Assert.True(result.DebuggerAttached, "SpineIDE didn't attach within 30 seconds");
        Assert.Null(result.Error);
        return result;
    }

    // the SpineIDE it starts gets the installed plugins too, for its completion lists
    private static Process? LaunchWithInstalledPlugins(Func<Process?> launch)
    {
        string? isolated = Environment.GetEnvironmentVariable(FishbonePluginLoader.PluginsDirectoryVariable);
        Environment.SetEnvironmentVariable(FishbonePluginLoader.PluginsDirectoryVariable, IsolatedPlugins.MachineDirectory);
        try
        {
            return launch();
        }
        finally
        {
            Environment.SetEnvironmentVariable(FishbonePluginLoader.PluginsDirectoryVariable, isolated);
        }
    }

    // SPINEIDE_PATH when it's set, otherwise the IDE built next to this test, in the same configuration
    private static string FindSpineIde()
    {
        if (Environment.GetEnvironmentVariable("SPINEIDE_PATH") is { Length: > 0 } fromEnvironment)
            return fromEnvironment;

        // bin/<configuration>/net8.0 under the test project
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string ide = Path.Combine(RepositoryRoot().FullName, "ide", "Fishbone.SpineIDE.Win32", "bin", configuration, "net8.0", "spineide.exe");
        Assert.True(File.Exists(ide), $"build ide/Fishbone.SpineIDE.Win32 ({configuration}) first, or set SPINEIDE_PATH: {ide}");
        return ide;
    }

    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Fishbone.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        return root;
    }

    private sealed class Picture;

    private sealed record Shapes(bool Squares);

    // gray, darker at the top left
    private static FishboneImage Gradient()
    {
        const int width = 320, height = 240;
        var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = (byte)((x + y) * 255 / (width + height));
        return new FishboneImage(width, height, 1, pixels);
    }

    // two squares, one run per row
    private static FishboneImage SquareRegions()
    {
        static FishboneRegion Square(int top, int left, int size) => new(
            Enumerable.Range(top, size).ToArray(),
            Enumerable.Repeat(left, size).ToArray(),
            Enumerable.Repeat(left + size - 1, size).ToArray());
        return FishboneImage.FromShapes([Square(40, 40, 60), Square(140, 180, 50)], []);
    }

    // a closed circle around the middle
    private static FishboneImage RingContour()
    {
        double[] angles = Enumerable.Range(0, 65).Select(i => i * 2 * Math.PI / 64).ToArray();
        return FishboneImage.FromShapes([],
            [new FishboneContour(angles.Select(a => 120 + 80 * Math.Sin(a)).ToArray(), angles.Select(a => 160 + 80 * Math.Cos(a)).ToArray())]);
    }
}
