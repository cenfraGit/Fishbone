namespace Fishbone.Tests;

public class TypedCollectionArgumentTests
{
    private static FishboneConfiguration Config() => new FishboneConfiguration()
        .AddBuiltIn("sumArray", new Func<int[], int>(values => values.Sum()))
        .AddBuiltIn("sumList", new Func<List<int>, int>(values => values.Sum()))
        .AddBuiltIn("sumIList", new Func<IList<int>, int>(values => values.Sum()))
        .AddBuiltIn("sumICollection", new Func<ICollection<int>, int>(values => values.Sum()))
        .AddBuiltIn("sumEnumerable", new Func<IEnumerable<int>, int>(values => values.Sum()))
        .AddBuiltIn("sumReadOnlyList", new Func<IReadOnlyList<int>, int>(values => values.Sum()))
        .AddBuiltIn("sumDoubles", new Func<double[], double>(values => values.Sum()))
        .AddBuiltIn("joinStrings", new Func<string?[], string>(values => string.Join(",", values.Select(v => v ?? "null"))))
        .AddBuiltIn("arrayType", new Func<int[], string>(values => values.GetType().Name))
        .AddBuiltIn("countArray", new Func<int[], int>(values => values.Length))
        .AddBuiltIn("overwriteFirst", new Func<int[], int>(values => values[0] = 99))
        .AddBuiltIn("addToObjectList", new Action<List<object>>(values => values.Add(4)));

    [Theory]
    [InlineData("sumArray")]
    [InlineData("sumList")]
    [InlineData("sumIList")]
    [InlineData("sumICollection")]
    [InlineData("sumEnumerable")]
    [InlineData("sumReadOnlyList")]
    public void Run_ListToTypedCollection_Converts(string function)
    {
        var env = FishboneProgram.Run($"let total = {function}([1, 2, 3]);", Config());

        Assert.Equal(6, env.GetValue("total"));
    }

    [Fact]
    public void Run_ListToArray_IsRealTypedArray()
    {
        var env = FishboneProgram.Run("let type = arrayType([1, 2]);", Config());

        Assert.Equal("Int32[]", env.GetValue("type"));
    }

    [Fact]
    public void Run_ListElements_FollowArgumentConversionRules()
    {
        var env = FishboneProgram.Run("""
let wholeDoubles = sumArray([1, 2.0, 4 / 2]);
let intsToDoubles = sumDoubles([1.5, 2]);
let withNull = joinStrings(["a", null]);
""", Config());

        Assert.Equal(5, env.GetValue("wholeDoubles"));
        Assert.Equal(3.5, env.GetValue("intsToDoubles"));
        Assert.Equal("a,null", env.GetValue("withNull"));
    }

    [Theory]
    [InlineData("let r = sumArray([1, 2.5]);")]
    [InlineData("""let r = sumArray([1, "2"]);""")]
    [InlineData("let r = sumArray([1, null]);")]
    [InlineData("let r = sumList([1, 3000000000]);")]
    [InlineData("""let r = joinStrings([1, "a"]);""")]
    public void Run_ListElementThatDoesNotConvert_RaisesError(string code)
    {
        Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(code, Config()));
    }

    [Fact]
    public void Run_ListElementThatDoesNotConvert_ErrorNamesElement()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let r = sumArray([1, 2, 2.5]);", Config()));

        // the third element is the one that fails
        Assert.Contains("element 3", exception.Message);
    }

    [Fact]
    public void Run_ConversionError_ShowsGenericTypeNames()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let r = sumList(5);", Config()));

        Assert.DoesNotContain("`1", exception.Message);
        Assert.Contains("List<", exception.Message);
    }

    [Fact]
    public void Run_EmptyList_ConvertsToEmptyCollection()
    {
        var env = FishboneProgram.Run("let count = countArray([]);", Config());

        Assert.Equal(0, env.GetValue("count"));
    }

    [Fact]
    public void Run_ListToTypedCollection_IsACopy()
    {
        // the .NET method gets a new array, so its writes don't reach the script's list
        var env = FishboneProgram.Run("""
let values = [1, 2, 3];
overwriteFirst(values);
let first = values[0];
""", Config());

        Assert.Equal(1, env.GetValue("first"));
    }

    [Fact]
    public void Run_ListToObjectList_IsNotCopied()
    {
        // a script list already is a List<object>, so it passes as is
        var env = FishboneProgram.Run("""
let values = [1, 2, 3];
addToObjectList(values);
let count = values.Count;
""", Config());

        Assert.Equal(4, env.GetValue("count"));
    }
}