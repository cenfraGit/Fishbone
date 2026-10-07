namespace Fishbone.Tests;

public class StaticMemberAccessTests
{
    // a static class can't be a generic argument, so it's registered with typeof
    private static FishboneConfiguration Config() => new FishboneConfiguration()
        .AddType<Settings>()
        .AddType(typeof(Calc))
        .AddValue("settings", new Settings());

    [Fact]
    public void Run_StaticFieldsAndProperties_ReadThroughTypeName()
    {
        var env = FishboneProgram.Run("""
let version = Settings.Version;
let kind = Settings.Kind;
let name = Settings.Name;
""", Config());

        Assert.Equal(3, env.GetValue("version"));
        Assert.Equal("demo", env.GetValue("kind"));
        Assert.Equal("settings", env.GetValue("name"));
    }

    [Fact]
    public void Run_StaticMethod_CallsThroughTypeName()
    {
        var env = FishboneProgram.Run("let sum = Calc.Add(1, 2);", Config());

        Assert.Equal(3, env.GetValue("sum"));
    }

    [Fact]
    public void Run_StaticMethodOverloads_PickBestMatch()
    {
        var env = FishboneProgram.Run("""
let fromInt = Calc.Pick(1);
let fromString = Calc.Pick("a");
""", Config());

        Assert.Equal("int", env.GetValue("fromInt"));
        Assert.Equal("string", env.GetValue("fromString"));
    }

    [Fact]
    public void Run_StaticMethodWithOutParameter_WritesBack()
    {
        var env = FishboneProgram.Run("let ok = Calc.TryHalf(4, out half);", Config());

        Assert.Equal(true, env.GetValue("ok"));
        Assert.Equal(2, env.GetValue("half"));
    }

    [Fact]
    public void Run_StaticMethodAsValue_CanBeCalledLater()
    {
        var env = FishboneProgram.Run("""
let add = Calc.Add;
let sum = add(1, 2);
""", Config());

        Assert.Equal(3, env.GetValue("sum"));
    }

    [Fact]
    public void Run_StaticAndInstanceMembers_StaySeparate()
    {
        // the reflection cache is shared across runs, so check both orders: a static
        // lookup must not leak into instance access, and an instance miss must not
        // hide the static member
        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let x = settings.Name;", Config()));
        var env = FishboneProgram.Run("let x = Settings.Name;", Config());
        Assert.Equal("settings", env.GetValue("x"));

        env = FishboneProgram.Run("let x = Settings.Version;", Config());
        Assert.Equal(3, env.GetValue("x"));
        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let x = settings.Version;", Config()));
    }

    [Fact]
    public void Run_InstanceMembersThroughTypeName_RaiseError()
    {
        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let x = Settings.InstanceValue;", Config()));
        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let x = Settings.InstanceMethod();", Config()));
    }

    [Fact]
    public void Run_UnknownStaticMember_RaisesErrorNamingTypeAndMember()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let x = Calc.Nope;", Config()));

        Assert.Contains("Calc", exception.Message);
        Assert.Contains("Nope", exception.Message);
    }

    [Fact]
    public void Run_StaticClassCalledAsConstructor_RaisesError()
    {
        var exception = Assert.ThrowsAny<Exception>(() =>
            FishboneProgram.Run("let c = Calc();", Config()));

        Assert.Contains("Calc", exception.Message);
    }

    [Fact]
    public void Run_MemberAccessDisabled_StaticMemberIsRuntimeError()
    {
        var config = Config();
        config.EnableMemberAccess = false;

        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let sum = Calc.Add(1, 2);", config));

        Assert.Contains("disabled by the host configuration", exception.Message);
    }

    private sealed class Settings
    {
        public const string Kind = "demo";
        public static readonly int Version = 3;
        public static string Name { get; } = "settings";

        public int InstanceValue { get; } = 7;
        public string InstanceMethod() => "instance";
    }

    private static class Calc
    {
        public static int Add(int a, int b) => a + b;

        public static string Pick(int value) => "int";
        public static string Pick(string value) => "string";

        public static bool TryHalf(int value, out int half)
        {
            half = value / 2;
            return value % 2 == 0;
        }
    }
}