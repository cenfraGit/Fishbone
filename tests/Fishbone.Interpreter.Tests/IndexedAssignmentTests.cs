namespace Fishbone.Interpreter.Tests;

public class IndexedAssignmentTests
{
    [Fact]
    public void Evaluate_IndexedAssignment_UpdatesListsAndDictionaries()
    {
        var env = InterpreterTestHelpers.Run("""
let values = [10, 20, 30];
values[1] = 99;
let keyed = {"existing": 1};
keyed["existing"] = 2;
keyed["new"] = 3;
""");

        var values = Assert.IsType<List<object>>(env.GetValue("values"));
        Assert.Equal([10, 99, 30], values);

        var keyed = Assert.IsType<Dictionary<object, object?>>(env.GetValue("keyed"));
        Assert.Equal(2, keyed["existing"]);
        Assert.Equal(3, keyed["new"]);
    }

    [Fact]
    public void Evaluate_IndexedAssignment_UpdatesNestedCollections()
    {
        var env = InterpreterTestHelpers.Run("""
let matrix = [[1, 2], [3, 4]];
matrix[0][1] = 20;
let nested = {"items": [5, 6]};
nested["items"][0] = 50;
""");

        var matrix = Assert.IsType<List<object>>(env.GetValue("matrix"));
        Assert.Equal([1, 20], Assert.IsType<List<object>>(matrix[0]));

        var nested = Assert.IsType<Dictionary<object, object?>>(env.GetValue("nested"));
        Assert.Equal([50, 6], Assert.IsType<List<object>>(nested["items"]));
    }

    [Theory]
    [InlineData("let value = null; value[0] = 1;", "Cannot assign through an index on null.")]
    [InlineData("let value = 10; value[0] = 1;", "Object of type 'Int32' does not support indexed assignment.")]
    [InlineData("let value = [1]; value[5] = 1;", "Index was out of range.")]
    public void Evaluate_InvalidIndexedAssignment_Throws(string code, string message)
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => InterpreterTestHelpers.Run(code));

        Assert.StartsWith(message, exception.Message);
    }
}