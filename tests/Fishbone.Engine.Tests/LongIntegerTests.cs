using Fishbone.Parser;

namespace Fishbone.Engine.Tests;

public class LongIntegerTests
{
    [Fact]
    public void Run_LargeIntegerLiteral_PromotesToLong()
    {
        var env = FishboneEngine.Run("""
let big = 999_999_999_999;
let small = 42;
""", new FishboneConfiguration());

        Assert.Equal(999_999_999_999L, env.GetValue("big"));
        Assert.IsType<long>(env.GetValue("big"));
        Assert.IsType<int>(env.GetValue("small"));
    }

    [Fact]
    public void Run_LongArithmeticAndComparisons_Work()
    {
        var env = FishboneEngine.Run("""
let big = 4_000_000_000;
let sum = big + 1;
let product = big * 2;
let mixed = big + 10.5;
let half = big / 2;
let isLarger = big > 2_000_000_000;
let equalsInt = 5_000_000_000 - 4_999_999_958 == 42;
""", new FishboneConfiguration());

        Assert.Equal(4_000_000_001L, env.GetValue("sum"));
        Assert.Equal(8_000_000_000L, env.GetValue("product"));
        Assert.Equal(4_000_000_010.5, env.GetValue("mixed"));
        Assert.Equal(2_000_000_000.0, env.GetValue("half")); // '/' is always true division
        Assert.Equal(true, env.GetValue("isLarger"));
        Assert.Equal(true, env.GetValue("equalsInt"));
    }

    [Fact]
    public void Run_LongIsTruthy_AndInterpolates()
    {
        var env = FishboneEngine.Run("""
let big = 5_000_000_000;
let truthy = false;
if (big) { truthy = true; }
let msg = $"big={big}";
""", new FishboneConfiguration());

        Assert.Equal(true, env.GetValue("truthy"));
        Assert.Equal("big=5000000000", env.GetValue("msg"));
    }

    [Fact]
    public void Parse_LiteralTooLargeForLong_IsParseError()
    {
        var exception = Assert.Throws<FishboneParseException>(
            () => FishboneEngine.Run("let x = 99_999_999_999_999_999_999;", new FishboneConfiguration()));

        Assert.Contains("too large for a 64-bit integer", exception.Message);
    }
}