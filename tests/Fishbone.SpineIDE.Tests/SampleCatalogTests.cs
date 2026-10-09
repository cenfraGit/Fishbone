using System.Text;
using Fishbone;
using SpineIDE.Services;

namespace SpineIDE.Tests;

public class SampleCatalogTests
{
    [Fact]
    public void Samples_AreDiscoveredFromEmbeddedResources()
    {
        Assert.NotEmpty(SampleCatalog.Samples);
        // display name derived from the file name
        Assert.Contains(SampleCatalog.Samples,
            sample => sample.FileName == "bubble_sort.fb" && sample.DisplayName == "Bubble Sort");
        // display name from the sample's "// title:" header
        Assert.Contains(SampleCatalog.Samples,
            sample => sample.FileName == "edge_detect.fb" && sample.DisplayName == "OpenCV Edge Detection");
    }

    [Fact]
    public void EveryDiscoveredSample_LoadsAndParses()
    {
        foreach (SampleDefinition sample in SampleCatalog.Samples)
        {
            Assert.False(string.IsNullOrWhiteSpace(sample.DisplayName));
            string code = SampleCatalog.Load(sample.FileName);
            Assert.False(string.IsNullOrWhiteSpace(code));
            FishboneProgram.FromSourceCode(code); // throws if a sample has syntax-rotted
        }
    }

    [Fact]
    public void Load_UnknownSample_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => SampleCatalog.Load("missing.fb"));
    }

    [Fact]
    public void BubbleSort_ExecutesAndSortsTheList()
    {
        string code = SampleCatalog.Load("bubble_sort.fb");
        var output = new StringBuilder();
        var configuration = CreateOutputConfiguration(output);

        FishboneProgram.Run(code, configuration);

        Assert.Contains("3, 12, 21, 54, 89", output.ToString());
    }

    [Fact]
    public void AreaCircle_ExecutesWithConfiguredInput()
    {
        string code = SampleCatalog.Load("area_circle.fb");
        var output = new StringBuilder();
        var configuration = CreateOutputConfiguration(output);
        configuration.AddBuiltIn("input", new Func<string>(() => "2"));

        FishboneProgram.Run(code, configuration);

        Assert.Contains("12.566", output.ToString());
    }

    private static FishboneConfiguration CreateOutputConfiguration(StringBuilder output)
    {
        var configuration = new FishboneConfiguration();
        configuration.AddBuiltIn("print", new Action<object?>(value => output.Append(value)));
        configuration.AddBuiltIn("println", new Action<object?>(value => output.AppendLine(value?.ToString())));
        return configuration;
    }
}
