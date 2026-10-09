using System.Globalization;

namespace Fishbone.Tests;

public class StringVariantTests
{
    [Fact]
    public void Run_RawString_IsVerbatim()
    {
        var env = FishboneProgram.Run(""""
let path = @"C:\temp\new";
let quoted = @"say ""hi""";
"""", new FishboneConfiguration());

        Assert.Equal(@"C:\temp\new", env.GetValue("path"));
        Assert.Equal("say \"hi\"", env.GetValue("quoted"));
    }

    [Fact]
    public void Run_InterpolatedString_EvaluatesHoles()
    {
        var env = FishboneProgram.Run("""
let name = "Fishbone";
let age = 2;
let msg = $"hello {name}, next year: {age + 1}";
""", new FishboneConfiguration());

        Assert.Equal("hello Fishbone, next year: 3", env.GetValue("msg"));
    }

    [Fact]
    public void Run_InterpolatedString_NullBecomesEmpty_AndNumbersUseInvariantCulture()
    {
        // de-DE would format 3.14 as "3,14"
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var env = FishboneProgram.Run("""
let nothing = null;
let msg = $"[{nothing}] pi={3.14}";
""", new FishboneConfiguration());

            Assert.Equal("[] pi=3.14", env.GetValue("msg"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Run_InterpolatedString_HoleMayCallMethodsAndIndex()
    {
        var env = FishboneProgram.Run("""
let d = {"key": "value"};
let xs = [10, 20];
let msg = $"d={d["key"]} x={xs[1]} up={"abc".ToUpper()}";
""", new FishboneConfiguration());

        Assert.Equal("d=value x=20 up=ABC", env.GetValue("msg"));
    }

    [Fact]
    public void Run_InterpolatedString_DoubledBracesAndEscapes()
    {
        var env = FishboneProgram.Run("""
let msg = $"{{literal}}\t{1 + 1}";
""", new FishboneConfiguration());

        Assert.Equal("{literal}\t2", env.GetValue("msg"));
    }

    [Fact]
    public void Run_NestedInterpolatedString_InsideHole()
    {
        var env = FishboneProgram.Run("""
let inner = "world";
let msg = $"outer {$"inner {inner}"}!";
""", new FishboneConfiguration());

        Assert.Equal("outer inner world!", env.GetValue("msg"));
    }
}