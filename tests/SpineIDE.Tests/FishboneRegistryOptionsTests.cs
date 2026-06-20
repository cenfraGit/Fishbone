using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

public class FishboneRegistryOptionsTests
{
    [Fact]
    public void GetGrammar_KnownScope_ReturnsCachedGrammar()
    {
        var options = new FishboneRegistryOptions();

        var first = options.GetGrammar("source.fb");
        var second = options.GetGrammar("source.fb");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetGrammar_UnknownScope_ReturnsNull()
    {
        var options = new FishboneRegistryOptions();

        Assert.Null(options.GetGrammar("source.unknown"));
    }

    [Fact]
    public void GetTheme_ReturnsCachedTheme()
    {
        var options = new FishboneRegistryOptions();

        var defaultTheme = options.GetDefaultTheme();
        var namedTheme = options.GetTheme("source.fb");

        Assert.NotNull(defaultTheme);
        Assert.Same(defaultTheme, namedTheme);
    }
}