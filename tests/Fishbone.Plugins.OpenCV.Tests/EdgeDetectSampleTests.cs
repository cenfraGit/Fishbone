using OpenCvSharp;

namespace Fishbone.Plugins.OpenCV.Tests;

/// <summary>The shipped <c>edge_detect.fb</c> sample, run for real against a generated image.</summary>
public class EdgeDetectSampleTests
{
    [Fact]
    public void Sample_ReadsTheImage_AndWritesItsEdges()
    {
        string folder = Directory.CreateTempSubdirectory("fishbone-edges-").FullName;
        try
        {
            // a white square on black has edges to find
            using (var input = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(0)))
            {
                Cv2.Rectangle(input, new Rect(16, 16, 32, 32), Scalar.All(255), -1);
                Cv2.ImWrite(Path.Combine(folder, "input.png"), input);
            }

            // the sample uses paths relative to where it runs. forward slashes need no escaping in a script string
            string script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "edge_detect.fb"))
                .Replace("\"input.png\"", $"\"{Path.Combine(folder, "input.png").Replace('\\', '/')}\"")
                .Replace("\"edges.png\"", $"\"{Path.Combine(folder, "edges.png").Replace('\\', '/')}\"");
            var config = new FishboneConfiguration()
                .AddBuiltIn("println", new Action<object?>(_ => { }))
                .AddPlugin(new OpenCVPlugin());

            FishboneProgram.Run(script, config);

            using var edges = Cv2.ImRead(Path.Combine(folder, "edges.png"), ImreadModes.Grayscale);
            Assert.False(edges.Empty());
            Assert.True(Cv2.CountNonZero(edges) > 0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
