using SpineIDE.Panels;

namespace SpineIDE.Tests;

public class OutputPanelVMTests
{
    [Fact]
    public void AppendBatch_AccumulatesAndClearResetsOutput()
    {
        var output = new OutputPanelVM();

        output.AppendBatch("first");
        output.AppendBatch(" second");
        Assert.Equal("first second", output.PanelText);

        output.Clear();
        Assert.Equal(string.Empty, output.PanelText);

        output.AppendBatch("new");
        Assert.Equal("new", output.PanelText);
    }
}