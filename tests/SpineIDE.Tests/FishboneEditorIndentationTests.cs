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
    public void Dedent_RemovesOneIndentLevel()
    {
        var indent = FishboneEditorIndentation.Dedent("        ", 4);

        Assert.Equal("    ", indent);
    }

    [Fact]
    public void IsBetweenBraces_WhenCaretIsBetweenBraces_ReturnsTrue()
    {
        Assert.True(FishboneEditorIndentation.IsBetweenBraces("{}", 1));
    }

    [Fact]
    public void EndsWithOpenBrace_IgnoresBracesInsideStringsAndComments()
    {
        Assert.False(FishboneEditorIndentation.EndsWithOpenBrace("""println("{");"""));
        Assert.False(FishboneEditorIndentation.EndsWithOpenBrace("println(value); // {"));
        Assert.False(FishboneEditorIndentation.EndsWithOpenBrace("println(value); /* { */"));
    }

    [Fact]
    public void IndentForNewLine_IgnoresBracesInsideMultilineBlockComments()
    {
        string text = """
/*
    {
""";
        int lineStart = text.LastIndexOf("    {", StringComparison.Ordinal);

        var indent = FishboneEditorIndentation.IndentForNewLine(text, lineStart, text.Length, 4);

        Assert.Equal("    ", indent);
    }

    [Fact]
    public void ShouldDedentClosingBrace_WhenOnlyWhitespaceBeforeBrace_ReturnsTrue()
    {
        Assert.True(FishboneEditorIndentation.ShouldDedentClosingBrace("        "));
        Assert.False(FishboneEditorIndentation.ShouldDedentClosingBrace("        value"));
    }
}
