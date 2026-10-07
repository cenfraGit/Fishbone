namespace Fishbone.Tests;

public class EnumMemberAccessTests
{
    private static FishboneConfiguration Config() => new FishboneConfiguration()
        .AddType<DayOfWeek>()
        .AddBuiltIn("takeDay", new Func<DayOfWeek, DayOfWeek>(value => value))
        .AddBuiltIn("getMonday", new Func<DayOfWeek>(() => DayOfWeek.Monday));

    [Fact]
    public void Run_EnumMember_ReturnsEnumValue()
    {
        var env = FishboneProgram.Run("let day = DayOfWeek.Monday;", Config());

        Assert.Equal(DayOfWeek.Monday, env.GetValue("day"));
    }

    [Fact]
    public void Run_EnumValues_CompareByValue()
    {
        var env = FishboneProgram.Run("""
let day = DayOfWeek.Monday;
let same = day == DayOfWeek.Monday;
let different = day != DayOfWeek.Friday;
let notANumber = day == 1;
""", Config());

        Assert.Equal(true, env.GetValue("same"));
        Assert.Equal(true, env.GetValue("different"));
        // an enum isn't a number, so it never equals one
        Assert.Equal(false, env.GetValue("notANumber"));
    }

    [Fact]
    public void Run_EnumMember_PassesToDotNetParameter()
    {
        var env = FishboneProgram.Run("let day = takeDay(DayOfWeek.Friday);", Config());

        Assert.Equal(DayOfWeek.Friday, env.GetValue("day"));
    }

    [Fact]
    public void Run_EnumReturnedFromDotNet_EqualsEnumMember()
    {
        var env = FishboneProgram.Run("let isMonday = getMonday() == DayOfWeek.Monday;", Config());

        Assert.Equal(true, env.GetValue("isMonday"));
    }

    [Fact]
    public void Run_EnumMember_PrintsName()
    {
        var env = FishboneProgram.Run("""let text = $"{DayOfWeek.Monday}";""", Config());

        Assert.Equal("Monday", env.GetValue("text"));
    }

    [Fact]
    public void Run_EnumRegisteredWithAlias_UsesAlias()
    {
        var config = new FishboneConfiguration().AddType<DayOfWeek>("Day");

        var env = FishboneProgram.Run("let day = Day.Monday;", config);

        Assert.Equal(DayOfWeek.Monday, env.GetValue("day"));
    }

    [Fact]
    public void Run_UnknownEnumMember_RaisesErrorNamingTypeAndMember()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let day = DayOfWeek.Mondy;", Config()));

        Assert.Contains("DayOfWeek", exception.Message);
        Assert.Contains("Mondy", exception.Message);
    }

    [Fact]
    public void Run_MemberAccessDisabled_EnumMemberIsRuntimeError()
    {
        var config = Config();
        config.EnableMemberAccess = false;

        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let day = DayOfWeek.Monday;", config));

        Assert.Contains("disabled by the host configuration", exception.Message);
    }
}