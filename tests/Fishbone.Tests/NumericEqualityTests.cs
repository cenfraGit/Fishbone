namespace Fishbone.Tests;

/// <summary>
/// == compares numbers by value across all numeric types. Integers compare exactly, and only
/// go through double when one side is a floating-point value. Dictionary keys don't do this:
/// they compare like a .NET Dictionary&lt;object, ...&gt;.
/// </summary>
public class NumericEqualityTests
{
    [Fact]
    public void Run_ScriptDictionary_KeysCompareLikeDotNet()
    {
        // like a c# Dictionary<object, ...>, an int key and a double key are different keys
        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("""
let d = {1: "a"};
let value = d[1.0];
""", new FishboneConfiguration()));
    }

    [Fact]
    public void Run_ScriptDictionary_CastKeyToInt_FindsKey()
    {
        var env = FishboneProgram.Run("""
let d = {1: "a"};
let value = d[(2 / 2) as int];
""", new FishboneConfiguration());

        Assert.Equal("a", env.GetValue("value"));
    }

    [Fact]
    public void Run_Equality_TreatsAllNumericTypesAsNumbers()
    {
        var config = new FishboneConfiguration()
            .AddValue("oneFloat", 1f)
            .AddValue("halfFloat", 0.5f)
            .AddValue("oneByte", (byte)1)
            .AddValue("oneShort", (short)1)
            .AddValue("oneDecimal", 1m);

        var env = FishboneProgram.Run("""
let floatEqual = 1 == oneFloat;
let halfEqual = 0.5 == halfFloat;
let byteEqual = 1 == oneByte;
let shortEqual = oneShort == 1;
let decimalEqual = 1 == oneDecimal;
let floatNotEqual = 2 != oneFloat;
""", config);

        Assert.Equal(true, env.GetValue("floatEqual"));
        Assert.Equal(true, env.GetValue("halfEqual"));
        Assert.Equal(true, env.GetValue("byteEqual"));
        Assert.Equal(true, env.GetValue("shortEqual"));
        Assert.Equal(true, env.GetValue("decimalEqual"));
        Assert.Equal(true, env.GetValue("floatNotEqual"));
    }

    [Fact]
    public void Run_Equality_ComparesLargeIntegersExactly()
    {
        var env = FishboneProgram.Run("""
let different = 9007199254740993 == 9007199254740992;
let same = 9007199254740993 == 9007199254740993;
""", new FishboneConfiguration());

        Assert.Equal(false, env.GetValue("different"));
        Assert.Equal(true, env.GetValue("same"));
    }

    [Fact]
    public void Run_Equality_CharComparesAsNumber()
    {
        // like c#, and like + and < already do
        var env = FishboneProgram.Run("""
let c = 65 as char;
let withInt = c == 65;
let withDouble = 65.0 == c;
let withChar = c == (65 as char);
let notEqual = c != 66;
""", new FishboneConfiguration());

        Assert.Equal(true, env.GetValue("withInt"));
        Assert.Equal(true, env.GetValue("withDouble"));
        Assert.Equal(true, env.GetValue("withChar"));
        Assert.Equal(true, env.GetValue("notEqual"));
    }

    [Fact]
    public void Run_Equality_NonNumbersAreNeverNumbers()
    {
        var env = FishboneProgram.Run("""
let boolAndInt = true == 1;
let stringAndInt = "1" == 1;
""", new FishboneConfiguration());

        Assert.Equal(false, env.GetValue("boolAndInt"));
        Assert.Equal(false, env.GetValue("stringAndInt"));
    }
}
