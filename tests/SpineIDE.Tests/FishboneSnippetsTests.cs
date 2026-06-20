using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

public class FishboneSnippetsTests
{
    [Fact]
    public void CoreSnippets_HaveLabelsAndTemplates()
    {
        Assert.NotEmpty(FishboneSnippets.Core);
        Assert.All(FishboneSnippets.Core, snippet =>
        {
            Assert.False(string.IsNullOrWhiteSpace(snippet.Header));
            Assert.False(string.IsNullOrWhiteSpace(snippet.Template));
            Assert.Contains("$caret$", snippet.Template);
        });
    }

    [Fact]
    public void CoreSnippets_IncludeExpectedFishboneSyntax()
    {
        string[] headers = FishboneSnippets.Core.Select(snippet => snippet.Header).ToArray();

        Assert.Contains("let", headers);
        Assert.Contains("func", headers);
        Assert.Contains("if / else if / else", headers);
        Assert.DoesNotContain("if", headers);
        Assert.DoesNotContain("else if", headers);
        Assert.DoesNotContain("else", headers);
        Assert.Contains("while", headers);
        Assert.Contains("foreach", headers);
        Assert.Contains("return", headers);
        Assert.Contains("break", headers);
        Assert.Contains("continue", headers);
        Assert.Contains("list", headers);
        Assert.Contains("dictionary", headers);
        Assert.Contains("import", headers);
    }

    [Fact]
    public void Prepare_RemovesCaretMarkerAndComputesCaretOffset()
    {
        var prepared = FishboneSnippets.Prepare("let $caret$name = value;", "");

        Assert.Equal("let name = value;", prepared.Text);
        Assert.Equal("let ".Length, prepared.CaretOffset);
    }

    [Fact]
    public void Prepare_IndentsMultilineSnippetsAfterFirstLine()
    {
        var prepared = FishboneSnippets.Prepare("""
if ($caret$condition)
{

}
""", "    ");

        string expected = string.Join(Environment.NewLine,
            "if (condition)",
            "    {",
            "    ",
            "    }");

        Assert.Equal(expected, prepared.Text);
        Assert.Equal("if (".Length, prepared.CaretOffset);
    }

    [Fact]
    public void Prepare_CaretOffsetIncludesInsertedIndentBeforeMarker()
    {
        var prepared = FishboneSnippets.Prepare("""
if (condition)
{
    $caret$
}
""", "    ");

        string expectedPrefix = string.Join(Environment.NewLine,
            "if (condition)",
            "    {",
            "        ");

        Assert.Equal(expectedPrefix.Length, prepared.CaretOffset);
    }
}