namespace Fishbone.Tests;

/// <summary>
/// C# keyword type names (int, double, string, ...) always refer to the type. Scripts can
/// call their static members, and the names are reserved so nothing can shadow them.
/// </summary>
public class KeywordTypeTests
{
    [Fact]
    public void Run_KeywordTypeStaticMembers_CallThroughKeyword()
    {
        var env = FishboneProgram.Run("""
let parsed = int.Parse("42");
let big = long.Parse("3000000000");
let flag = bool.Parse("true");
let max = int.MaxValue;
let empty = string.IsNullOrEmpty("");
""", new FishboneConfiguration());

        Assert.Equal(42, env.GetValue("parsed"));
        Assert.Equal(3000000000L, env.GetValue("big"));
        Assert.Equal(true, env.GetValue("flag"));
        Assert.Equal(int.MaxValue, env.GetValue("max"));
        Assert.Equal(true, env.GetValue("empty"));
    }

    [Fact]
    public void Run_KeywordTypeTryParse_WritesOutValue()
    {
        var env = FishboneProgram.Run("""
let ok = int.TryParse("7", out value);
let bad = int.TryParse("oops", out other);
""", new FishboneConfiguration());

        Assert.Equal(true, env.GetValue("ok"));
        Assert.Equal(7, env.GetValue("value"));
        Assert.Equal(false, env.GetValue("bad"));
        Assert.Equal(0, env.GetValue("other"));
    }

    [Theory]
    [InlineData("let int = 5;")]
    [InlineData("let x = 1; double = 5;")]
    [InlineData("func string() { return 1; }")]
    [InlineData("func f(bool) { return 1; }")]
    [InlineData("for (long in 0, 5) { }")]
    [InlineData("foreach (char in [1, 2]) { }")]
    [InlineData("try { throw 1; } catch (object) { }")]
    [InlineData("""let ok = int.TryParse("7", out decimal);""")]
    public void Run_KeywordTypeNameDeclared_IsParseError(string code)
    {
        var exception = Assert.Throws<FishboneParseException>(() =>
            FishboneProgram.Run(code, new FishboneConfiguration()));

        Assert.Contains("reserved", exception.Message);
    }

    [Fact]
    public void Run_NamesThatOnlyContainKeywordTypes_AreAllowed()
    {
        // the check is on the whole name, and it's case-sensitive like C#
        var env = FishboneProgram.Run("""
let integer = 1;
let intValue = 2;
let Int = 3;
""", new FishboneConfiguration());

        Assert.Equal(1, env.GetValue("integer"));
        Assert.Equal(2, env.GetValue("intValue"));
        Assert.Equal(3, env.GetValue("Int"));
    }

    [Fact]
    public void Configuration_KeywordTypeName_IsRejected()
    {
        var config = new FishboneConfiguration();

        Assert.ThrowsAny<ArgumentException>(() => config.AddValue("int", 5));
        Assert.ThrowsAny<ArgumentException>(() => config.AddBuiltIn("double", new Func<int>(() => 1)));
        Assert.ThrowsAny<ArgumentException>(() => config.AddType<Version>("string"));
    }

    [Fact]
    public void Configuration_TypeWhoseOwnNameIsReserved_IsRejected()
    {
        // with no name given, AddType uses the type's own name, which is checked too
        var config = new FishboneConfiguration();

        Assert.ThrowsAny<ArgumentException>(() => config.AddType<@string>());
    }

    private sealed class @string { }
}