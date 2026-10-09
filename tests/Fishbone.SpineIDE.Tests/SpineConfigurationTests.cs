using SpineIDE.Services;
using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

public class SpineConfigurationTests
{
    [Theory]
    [InlineData("print")]
    [InlineData("println")]
    [InlineData("input")]
    public void Description_HasTheBuiltInsARunRegisters(string name)
    {
        // the editor describes the same configuration a run uses, so these aren't missing
        var description = Assert.IsType<Fishbone.FishboneDescription>(SpineConfiguration.Description);

        var symbol = Assert.Single(description.Symbols, symbol => symbol.Name == name);
        Assert.Equal(Fishbone.FishboneSymbolKind.Function, symbol.Kind);
    }

    [Fact]
    public void Signature_ShowsKeywordTypeNames()
    {
        var println = Assert.Single(SpineConfiguration.Description!.Symbols, symbol => symbol.Name == "println");

        var signature = FishboneSignature.From("println", Assert.Single(println.Signatures));

        Assert.Equal("object", Assert.Single(signature.Parameters).Type);
        Assert.Equal("void", signature.ReturnType);
    }
}
