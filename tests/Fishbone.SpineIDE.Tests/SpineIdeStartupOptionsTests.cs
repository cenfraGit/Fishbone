using SpineIDE.Services;

namespace SpineIDE.Tests;

public class SpineIdeStartupOptionsTests
{
    [Fact]
    public void CommandLineParsesLocalAttachPort()
    {
        using var errors = new StringWriter();

        bool parsed = SpineIdeStartupOptions.TryParse(["--attach", "5123"], errors, out var options);

        Assert.True(parsed);
        Assert.Equal(5123, options.AttachPort);
        Assert.Equal(string.Empty, errors.ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void CommandLineRejectsInvalidAttachPort(string value)
    {
        using var errors = new StringWriter();

        bool parsed = SpineIdeStartupOptions.TryParse(["--attach", value], errors, out _);

        Assert.False(parsed);
        Assert.NotEmpty(errors.ToString());
    }
}
