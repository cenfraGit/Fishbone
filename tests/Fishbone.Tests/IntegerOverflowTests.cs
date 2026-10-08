namespace Fishbone.Tests;

/// <summary>
/// Integer arithmetic is checked, like C# <c>checked</c>: overflow is a runtime error instead
/// of a silent wrap. Literals keep their types (int, or long when they don't fit).
/// </summary>
public class IntegerOverflowTests
{
    [Theory]
    [InlineData("let r = 2147483647 + 1;")]
    [InlineData("let r = -2147483647 - 2;")]
    [InlineData("let r = 100000 * 100000;")]
    [InlineData("let r = int.MaxValue + 1;")]
    [InlineData("let min = -2147483647 - 1; let r = -min;")]
    [InlineData("let r = 9223372036854775807 + 1;")]
    [InlineData("let r = 3037000500 * 3037000500;")]
    [InlineData("let x = 2147483647; x += 1;")]
    public void Run_IntegerOverflow_RaisesError(string code)
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run(code, new FishboneConfiguration()));

        Assert.Contains("overflow", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_IntOverflow_MessagePointsToLong()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let r = 100000 * 100000;", new FishboneConfiguration()));

        Assert.Contains("int", exception.Message);
        Assert.Contains("as long", exception.Message);
    }

    [Fact]
    public void Run_CastToLongFirst_AvoidsIntOverflow()
    {
        var env = FishboneProgram.Run("""
let sum = (2147483647 as long) + 1;
let product = (100000 as long) * 100000;
""", new FishboneConfiguration());

        Assert.Equal(2147483648L, env.GetValue("sum"));
        Assert.Equal(10000000000L, env.GetValue("product"));
    }

    [Fact]
    public void Run_ArithmeticThatFits_KeepsIntType()
    {
        var env = FishboneProgram.Run("""
let max = 2147483646 + 1;
let min = -2147483647 - 1;
let product = 46340 * 46340;
let remainder = -5 % 3;
""", new FishboneConfiguration());

        Assert.Equal(int.MaxValue, Assert.IsType<int>(env.GetValue("max")));
        Assert.Equal(int.MinValue, Assert.IsType<int>(env.GetValue("min")));
        Assert.Equal(2147395600, Assert.IsType<int>(env.GetValue("product")));
        Assert.Equal(-2, Assert.IsType<int>(env.GetValue("remainder")));
    }

    [Fact]
    public void Run_DoubleArithmetic_NeverOverflows()
    {
        // doubles follow IEEE rules: too big becomes Infinity, not an error
        var env = FishboneProgram.Run("""
let mixed = 2147483647 + 1.0;
let product = double.MaxValue * 2;
""", new FishboneConfiguration());

        Assert.Equal(2147483648.0, env.GetValue("mixed"));
        Assert.Equal(double.PositiveInfinity, env.GetValue("product"));
    }
}