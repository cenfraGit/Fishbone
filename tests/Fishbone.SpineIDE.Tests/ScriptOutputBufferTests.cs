using SpineIDE.Panels;

namespace SpineIDE.Tests;

public class ScriptOutputBufferTests
{
    [Fact]
    public void AppendAndAppendLine_PreserveTextAndNewlines()
    {
        var buffer = new ScriptOutputBuffer();

        buffer.Append("first");
        buffer.AppendLine(" second");

        Assert.Equal("first second" + Environment.NewLine, buffer.DrainPending());
    }

    [Fact]
    public void DrainPending_ReturnsOnlyNewOutput()
    {
        var buffer = new ScriptOutputBuffer();
        buffer.Append("first");

        Assert.Equal("first", buffer.DrainPending());
        Assert.Equal(string.Empty, buffer.DrainPending());

        buffer.Append("second");
        Assert.Equal("second", buffer.DrainPending());
    }

    [Fact]
    public void ConcurrentAppends_DoNotCorruptOrLoseLines()
    {
        var buffer = new ScriptOutputBuffer();

        Parallel.For(0, 1000, i => buffer.AppendLine(i));

        string[] lines = buffer.DrainPending()
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1000, lines.Length);
        Assert.Equal(1000, lines.Distinct().Count());
    }

    [Fact]
    public void LargeOutput_IsRetained()
    {
        var buffer = new ScriptOutputBuffer();
        string output = new('x', 2_000_000);

        buffer.Append(output);

        Assert.Equal(output, buffer.DrainPending());
    }
}