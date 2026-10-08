using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

public class FishboneCompletionCatalogTests
{
    [Theory]
    [InlineData("print")]
    [InlineData("println")]
    [InlineData("input")]
    public void Catalog_OffersTheBuiltInsARunRegisters(string name)
    {
        // completion uses the same configuration as a run, so these aren't missing anymore
        var catalog = FishboneCompletionCatalog.Shared;

        var item = Assert.Single(catalog.Globals, global => global.Text == name);
        Assert.Equal(FishboneCompletionKind.Function, item.Kind);
        Assert.True(catalog.Signatures.ContainsKey(name));
    }

    [Fact]
    public void Catalog_ShowsKeywordTypeNames()
    {
        var signature = Assert.Single(FishboneCompletionCatalog.Shared.Signatures["println"]);

        Assert.Equal("object", Assert.Single(signature.Parameters).Type);
        Assert.Equal("void", signature.ReturnType);
    }
}
