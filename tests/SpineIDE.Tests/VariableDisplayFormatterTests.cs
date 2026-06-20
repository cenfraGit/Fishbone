using SpineIDE.Panels;

namespace SpineIDE.Tests;

public class VariableDisplayFormatterTests
{
    [Fact]
    public void FormatValue_Primitives_UseReadableDisplay()
    {
        Assert.Equal("5", VariableDisplayFormatter.FormatValue(5));
        Assert.Equal("hello", VariableDisplayFormatter.FormatValue("hello"));
        Assert.Equal("true", VariableDisplayFormatter.FormatValue(true));
        Assert.Equal("null", VariableDisplayFormatter.FormatValue(null));
    }

    [Fact]
    public void FormatValue_List_UsesFishboneLikeInlineDisplay()
    {
        var value = new List<object?> { 1, 2, 3 };

        Assert.Equal("[1, 2, 3]", VariableDisplayFormatter.FormatValue(value));
    }

    [Fact]
    public void FormatValue_Dictionary_UsesFishboneLikeInlineDisplay()
    {
        var value = new Dictionary<object, object?> { ["key"] = 5 };

        Assert.Equal("""{"key": 5}""", VariableDisplayFormatter.FormatValue(value));
    }

    [Fact]
    public void FormatValue_Collections_QuoteAndEscapeNestedStrings()
    {
        var value = new List<object?> { "plain", "quote\"", "line\nbreak" };

        Assert.Equal("""["plain", "quote\"", "line\nbreak"]""", VariableDisplayFormatter.FormatValue(value));
    }

    [Fact]
    public void FormatValue_Collections_AreCompactAndCapped()
    {
        var longList = new List<object?> { 1, 2, 3, 4, 5, 6 };
        var nested = new List<object?> { new List<object?> { new List<object?> { 1 } } };

        Assert.Equal("[1, 2, 3, 4, 5, ...]", VariableDisplayFormatter.FormatValue(longList));
        Assert.Equal("[[[...]]]", VariableDisplayFormatter.FormatValue(nested));
    }

    [Fact]
    public void FormatType_Collections_UseFriendlyNames()
    {
        Assert.Equal("List", VariableDisplayFormatter.FormatType(new List<object?>()));
        Assert.Equal("Dictionary", VariableDisplayFormatter.FormatType(new Dictionary<object, object?>()));
        Assert.Equal("Int32", VariableDisplayFormatter.FormatType(5));
    }

    [Fact]
    public void BuildDetailRows_List_IncludesIndexedPathsAndChildValues()
    {
        var rows = VariableDisplayFormatter.BuildDetailRows("items", new List<object?> { 1, "two" });

        Assert.Collection(rows,
            row =>
            {
                Assert.Equal("items", row.Path);
                Assert.Equal("List", row.Type);
                Assert.Equal("""[1, "two"]""", row.Value);
                Assert.Equal(0, row.Depth);
            },
            row =>
            {
                Assert.Equal("items[0]", row.Path);
                Assert.Equal("Int32", row.Type);
                Assert.Equal("1", row.Value);
                Assert.Equal(1, row.Depth);
            },
            row =>
            {
                Assert.Equal("items[1]", row.Path);
                Assert.Equal("String", row.Type);
                Assert.Equal("two", row.Value);
                Assert.Equal(1, row.Depth);
            });
    }

    [Fact]
    public void BuildDetailRows_Dictionary_IncludesKeyPathsAndNestedRows()
    {
        var value = new Dictionary<object, object?>
        {
            ["outer"] = new Dictionary<object, object?> { ["inner"] = 42 }
        };

        var rows = VariableDisplayFormatter.BuildDetailRows("map", value);

        Assert.Equal("map", rows[0].Path);
        Assert.Equal("""map["outer"]""", rows[1].Path);
        Assert.Equal("Dictionary", rows[1].Type);
        Assert.Equal("""map["outer"]["inner"]""", rows[2].Path);
        Assert.Equal("Int32", rows[2].Type);
        Assert.Equal("42", rows[2].Value);
        Assert.Equal(2, rows[2].Depth);
    }
}