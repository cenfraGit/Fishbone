using Fishbone.Core;

namespace Fishbone.Engine.Tests;

public class StaticMemberAccessTests
{
    public static class Tools
    {
        public static int Counter = 7;
        public static string Label => "tools";

        public static int Twice(int n) => n * 2;
        public static string Twice(string text) => text + text;

        public static bool TryHalve(int n, out int half)
        {
            half = n / 2;
            return n % 2 == 0;
        }

        public static T Echo<T>(T value) => value;
    }

    public sealed class Widget
    {
        public Widget() { }
        public Widget(string name) => Name = name;

        public string Name { get; } = "unnamed";
        public static string Describe() => "a widget";
    }

    public sealed class StaticFirstProbe
    {
        public string OnlyOnInstances => "instance value";
    }

    public sealed class InstanceFirstProbe
    {
        public string OnlyOnInstances => "instance value";
    }

    public enum Mode { Off, Fast, Slow }

    private static FishboneConfiguration Config() =>
        new FishboneConfiguration()
            .AddType(typeof(Tools), "tools")
            .AddType<Widget>()
            .AddType<Mode>()
            .AddType(typeof(StaticFirstProbe), "staticFirst")
            .AddType(typeof(InstanceFirstProbe), "instanceFirst");

    [Fact]
    public void Run_StaticMethod_IsCallableThroughRegisteredType()
    {
        var env = FishboneEngine.Run("let doubled = tools.Twice(21);", Config());

        Assert.Equal(42, env.GetValue("doubled"));
    }

    [Fact]
    public void Run_StaticMethodOverloads_ResolveByArgumentType()
    {
        var env = FishboneEngine.Run("""
let number = tools.Twice(4);
let text = tools.Twice("ab");
""", Config());

        Assert.Equal(8, env.GetValue("number"));
        Assert.Equal("abab", env.GetValue("text"));
    }

    [Fact]
    public void Run_StaticProperty_IsReadable()
    {
        var env = FishboneEngine.Run("let label = tools.Label;", Config());

        Assert.Equal("tools", env.GetValue("label"));
    }

    [Fact]
    public void Run_StaticField_IsReadable()
    {
        var env = FishboneEngine.Run("let counter = tools.Counter;", Config());

        Assert.Equal(7, env.GetValue("counter"));
    }

    [Fact]
    public void Run_StaticMethodWithOutParameter_WritesBackThroughNullTarget()
    {
        var env = FishboneEngine.Run("""
let even = tools.TryHalve(10, out half);
let odd = tools.TryHalve(7, out otherHalf);
""", Config());

        Assert.Equal(true, env.GetValue("even"));
        Assert.Equal(5, env.GetValue("half"));
        Assert.Equal(false, env.GetValue("odd"));
        Assert.Equal(3, env.GetValue("otherHalf"));
    }

    [Fact]
    public void Run_EnumConstant_IsReadableAsStaticField()
    {
        var env = FishboneEngine.Run("let mode = Mode.Fast;", Config());

        Assert.Equal(Mode.Fast, env.GetValue("mode"));
    }

    [Fact]
    public void Run_RegisteredType_SupportsConstructionAndStaticsTogether()
    {
        var env = FishboneEngine.Run("""
let made = Widget("gear");
let name = made.Name;
let described = Widget.Describe();
""", Config());

        Assert.Equal("gear", env.GetValue("name"));
        Assert.Equal("a widget", env.GetValue("described"));
    }

    [Fact]
    public void Run_UnknownStaticMember_NamesTheWrappedType()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(
            () => FishboneEngine.Run("let x = tools.Missing;", Config()));

        Assert.Contains("'Tools'", exception.Message);
        Assert.Contains("Missing", exception.Message);
    }

    [Fact]
    public void Run_GenericMethodDefinition_IsNotExposed()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(
            () => FishboneEngine.Run("let x = tools.Echo(1);", Config()));

        Assert.Contains("does not have a public member named 'Echo'", exception.Message);
    }

    [Fact]
    public void Run_GetTypeResult_StillExposesTypeInstanceMembers()
    {
        var env = FishboneEngine.Run("""
let text = "hi";
let typeName = text.GetType().Name;
""", Config());

        Assert.Equal("String", env.GetValue("typeName"));
    }

    [Fact]
    public void Run_StaticLookupBeforeInstanceLookup_DoesNotPoisonInstanceAccess()
    {
        var env = FishboneEngine.Run("""
let staticError = "";
try { let bad = staticFirst.OnlyOnInstances; } catch (e) { staticError = e.Message; }
let made = staticFirst();
let value = made.OnlyOnInstances;
""", Config());

        Assert.Contains("does not have a public member", (string)env.GetValue("staticError"));
        Assert.Equal("instance value", env.GetValue("value"));
    }

    [Fact]
    public void Run_InstanceLookupBeforeStaticLookup_DoesNotLeakInstanceMember()
    {
        var env = FishboneEngine.Run("""
let made = instanceFirst();
let value = made.OnlyOnInstances;
let staticError = "";
try { let bad = instanceFirst.OnlyOnInstances; } catch (e) { staticError = e.Message; }
""", Config());

        Assert.Equal("instance value", env.GetValue("value"));
        Assert.Contains("does not have a public member", (string)env.GetValue("staticError"));
    }
}