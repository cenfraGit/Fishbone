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
/// does for an app. The IDE stops on the first line. Step around, check picture and shapes into
/// the preview, then continue to the end, and the test checks what the script left behind.
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
        string ide = FindSpineIde();
        // attached, println goes to the IDE's output instead
        var config = new FishboneConfiguration()
            .AddBuiltIn("println", new Action<object?>(Console.WriteLine))
            .AddBuiltIn("picture", new Picture())
            .AddBuiltIn("squares", new Shapes(Squares: true))
            .AddBuiltIn("ring", new Shapes(Squares: false))
            .AddVisualizer<Picture>(_ => Gradient())
            .AddVisualizer<Shapes>(shapes => shapes.Squares ? SquareRegions() : RingContour());

        var result = await FishboneProgram.FromSourceCode(Script).RunDebuggableAsync(config, new FishboneDebugOptions
        {
            OpenIde = true,
            AttachTimeout = TimeSpan.FromSeconds(30),
            SourceName = "attach-test.fb",
            IdeLauncher = endpoint => Process.Start(ide, $"--attach {endpoint.Port}"),
        });

        Assert.True(result.DebuggerAttached, "SpineIDE didn't attach within 30 seconds");
        Assert.Null(result.Error);
        Assert.Equal(10, result.Environment!.GetValue("total"));
    }

    // SPINEIDE_PATH when it's set, otherwise the IDE built next to this test, in the same configuration
    private static string FindSpineIde()
    {
        if (Environment.GetEnvironmentVariable("SPINEIDE_PATH") is { Length: > 0 } fromEnvironment)
            return fromEnvironment;

        // bin/<configuration>/net8.0 under the test project
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        string configuration = output.Parent!.Name;
        DirectoryInfo? root = output;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Fishbone.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        string ide = Path.Combine(root.FullName, "ide", "Fishbone.SpineIDE.Win32", "bin", configuration, "net8.0", "spineide.exe");
        Assert.True(File.Exists(ide), $"build ide/Fishbone.SpineIDE.Win32 ({configuration}) first, or set SPINEIDE_PATH: {ide}");
        return ide;
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
