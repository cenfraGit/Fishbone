using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

public class FishboneLocalSymbolScannerTests
{
    private static string[] Names(string source) =>
        FishboneLocalSymbolScanner.Scan(source).Select(item => item.Text).ToArray();

    [Fact]
    public void Scan_CollectsLetVariablesAndFunctionNames()
    {
        var names = Names("""
let total = 0;
func add(left, right) { return left + right; }
""");

        Assert.Contains("total", names);
        Assert.Contains("add", names);
        Assert.Contains("left", names);
        Assert.Contains("right", names);
    }

    // the parameter name must be captured, not the out/ref keyword in front of it
    [Fact]
    public void Scan_ParameterDirectionKeywords_AreNotOfferedAsNames()
    {
        var names = Names("func divide(a, out quotient, ref remainder) { }");

        Assert.Contains("a", names);
        Assert.Contains("quotient", names);
        Assert.Contains("remainder", names);
        Assert.DoesNotContain("out", names);
        Assert.DoesNotContain("ref", names);
    }

    // a parameter that merely starts with "out"/"ref" must survive intact
    [Theory]
    [InlineData("func f(outer) { }", "outer")]
    [InlineData("func f(reference) { }", "reference")]
    public void Scan_ParameterNameStartingWithKeyword_IsNotTruncated(string source, string expected)
    {
        Assert.Contains(expected, Names(source));
    }
}
