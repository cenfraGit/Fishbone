namespace Fishbone.Tests;

/// <summary>
/// The for-loop variable keeps the type of its range: int when start, end and step are all
/// ints, long when they're all integers and one is a long, and double otherwise. Bounds must be
/// numbers, and a step that moves away from the end is an error.
/// </summary>
public class ForLoopTests
{
    // a loop that never ends trips this instead of hanging the test run
    private static FishboneConfiguration Config() => new FishboneConfiguration()
        .AddBuiltIn("guard", new Action<int>(count =>
        {
            if (count > 100)
                throw new InvalidOperationException("loop ran away");
        }));

    private static List<object> Values(string range)
    {
        var env = FishboneProgram.Run($$"""
let values = [];
for (i in {{range}}) { values.Add(i); guard(values.Count); }
""", Config());
        return Assert.IsType<List<object>>(env.GetValue("values"));
    }

    [Theory]
    [InlineData("0, 5", new[] { 0, 1, 2, 3, 4 })]
    [InlineData("0, 10, 3", new[] { 0, 3, 6, 9 })]
    [InlineData("3, 0", new[] { 3, 2, 1 })]
    [InlineData("10, 0, -4", new[] { 10, 6, 2 })]
    public void Run_IntRange_LoopVariableIsInt(string range, int[] expected)
    {
        var values = Values(range);

        Assert.All(values, value => Assert.IsType<int>(value));
        Assert.Equal(expected.Cast<object>(), values);
    }

    [Fact]
    public void Run_LongBound_LoopVariableIsLong()
    {
        var values = Values("0, 3000000000, 1000000000");

        Assert.Equal(new object[] { 0L, 1000000000L, 2000000000L }, values);
    }

    [Theory]
    [InlineData("0, 2, 0.5", new[] { 0.0, 0.5, 1.0, 1.5 })]
    [InlineData("0.0, 3", new[] { 0.0, 1.0, 2.0 })]
    [InlineData("0, 2.5", new[] { 0.0, 1.0, 2.0 })]
    [InlineData("1, 0, -0.25", new[] { 1.0, 0.75, 0.5, 0.25 })]
    public void Run_DoubleInRange_LoopVariableIsDouble(string range, double[] expected)
    {
        var values = Values(range);

        Assert.Equal(expected.Cast<object>(), values);
    }

    [Fact]
    public void Run_FractionalStep_DoesNotAccumulateError()
    {
        // adding 0.1 ten times gives 0.9999999999999999, which is still below 1. each value
        // is start + k * step instead, so the loop stops after ten
        var values = Values("0, 1, 0.1");

        Assert.Equal(10, values.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(k => (object)(k * 0.1)), values);
    }

    [Theory]
    [InlineData("2147483640, 2147483647, 5", new[] { 2147483640, 2147483645 })]
    [InlineData("-2147483641, -2147483648, -5", new[] { -2147483641, -2147483646 })]
    public void Run_IntRangeNearLimit_DoesNotOverflow(string range, int[] expected)
    {
        // the value after the last one doesn't fit in an int, but it only ends the loop
        var values = Values(range);

        Assert.Equal(expected.Cast<object>(), values);
    }

    [Fact]
    public void Run_LongRangeNearLimit_DoesNotOverflow()
    {
        var values = Values("9223372036854775800, 9223372036854775807, 5");

        Assert.Equal(new object[] { 9223372036854775800L, 9223372036854775805L }, values);
    }

    [Theory]
    [InlineData("""for (i in "0", "3") { }""")]
    [InlineData("for (i in null, 3) { }")]
    [InlineData("for (i in 0, null) { }")]
    [InlineData("""for (i in 0, 3, "1") { }""")]
    [InlineData("for (i in true, 3) { }")]
    public void Run_NonNumberBound_RaisesError(string code)
    {
        Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(code, Config()));
    }

    [Theory]
    [InlineData("0, 10, -1")]
    [InlineData("10, 0, 2")]
    [InlineData("0, 1, -0.5")]
    public void Run_StepAwayFromEnd_RaisesError(string range)
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => Values(range));

        Assert.Contains("step", exception.Message);
    }

    [Fact]
    public void Run_ZeroStep_RaisesErrorEvenForEmptyRange()
    {
        Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("for (i in 5, 5, 0) { }", Config()));
    }

    [Fact]
    public void Run_AssigningLoopVariable_DoesNotChangeIterations()
    {
        var env = FishboneProgram.Run("""
let count = 0;
for (i in 0, 3) { i = 100; count += 1; guard(count); }
""", Config());

        Assert.Equal(3, env.GetValue("count"));
    }
}