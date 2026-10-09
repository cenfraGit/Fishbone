using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

public class FishboneEditorIndentationTests
{
    [Fact]
    public void IndentForNewLine_PreservesPreviousLineIndent()
    {
        var indent = FishboneEditorIndentation.IndentForNewLine("        let x = 1;", 4);

        Assert.Equal("        ", indent);
    }

    [Fact]
    public void IndentForNewLine_AfterOpenBrace_IndentsOneLevel()
    {
        var indent = FishboneEditorIndentation.IndentForNewLine("    if (x) {", 4);

        Assert.Equal("        ", indent);
    }

    [Fact]
    public void IndentForNewLine_IgnoresBracesInsideStringsAndComments()
    {
        Assert.Equal("", FishboneEditorIndentation.IndentForNewLine("""println("{");""", 4));
        Assert.Equal("", FishboneEditorIndentation.IndentForNewLine("println(value); // {", 4));
        Assert.Equal("", FishboneEditorIndentation.IndentForNewLine("println(value); /* { */", 4));
    }

    [Fact]
    public void ShouldDedentClosingBrace_WhenOnlyWhitespaceBeforeBrace_ReturnsTrue()
    {
        Assert.True(FishboneEditorIndentation.ShouldDedentClosingBrace("        "));
        Assert.False(FishboneEditorIndentation.ShouldDedentClosingBrace("        value"));
    }
}
