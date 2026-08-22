using Fishbone.Parser;

namespace Fishbone.Engine.Tests;

public class ScientificNotationTests
{
    [Fact]
    public void Run_ScientificNotation_EvaluatesAndInterpolates()
    {
        var env = FishboneEngine.Run("""
let big = 1e3;
let tiny = 1e-400;
let msg = $"big={1e3}";
let signed = $"signed={1e+3}";
""", new FishboneConfiguration());

        Assert.Equal(1000.0, env.GetValue("big"));
        // an exponent below the double range underflows to zero, like C#/IEEE-754
        Assert.Equal(0.0, env.GetValue("tiny"));
        // interpolation holes are re-parsed through the same lexer, so exponents work there too
        Assert.Equal("big=1000", env.GetValue("msg"));
        Assert.Equal("signed=1000", env.GetValue("signed"));
    }

    [Fact]
    public void Parse_LiteralTooLargeForDouble_IsParseError()
    {
        var exception = Assert.Throws<FishboneParseException>(
            () => FishboneEngine.Run("let x = 1e400;", new FishboneConfiguration()));

        Assert.Contains("too large for a 64-bit double", exception.Message);
    }
}