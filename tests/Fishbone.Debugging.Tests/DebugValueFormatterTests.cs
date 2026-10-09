namespace Fishbone.Debugging.Tests;

public class DebugValueFormatterTests
{
    private sealed class Opaque;

    private sealed record Described(int Size);

    [Fact]
    public void AnObjectThatDoesntDescribeItself_ShowsItsShortTypeName()
    {
        Assert.Equal("Opaque", DebugValueFormatter.FormatValue(new Opaque()));
        Assert.Equal("Opaque", DebugValueFormatter.FormatType(new Opaque()));
    }

    [Fact]
    public void AnObjectThatDescribesItself_ShowsItsDescription()
    {
        Assert.Equal("Described { Size = 3 }", DebugValueFormatter.FormatValue(new Described(3)));
    }
}
