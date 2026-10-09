namespace Fishbone.Parser.Tests;

public class IndexedAssignmentMessageTests
{
    [Fact]
    public void Parse_NonIndexedAssignment_IncludesFoundTypeNameInMessage()
    {
        var exception = Assert.Throws<FishboneParseException>(() => ParserTestHelpers.ParseProgram("(a + b) = 10;"));

        Assert.Contains("BinaryOpNode", exception.Message);
        Assert.Contains("indexed target", exception.Message);
    }
}