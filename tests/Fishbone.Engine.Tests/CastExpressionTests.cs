namespace Fishbone.Engine.Tests;

public class CastExpressionTests
{
    [Fact]
    public void Run_CastConvertibleValues_ReturnsConvertedValue()
    {
        var env = FishboneProgram.Run("""
let fromString = "42" as int;
let fromDouble = 2.0 as int;
let toDouble = "3.5" as double;
let toString = 42 as string;
let toBool = "true" as bool;
""", new FishboneConfiguration());

        Assert.Equal(42, env.GetValue("fromString"));
        Assert.Equal(2, env.GetValue("fromDouble"));
        Assert.Equal(3.5, env.GetValue("toDouble"));
        Assert.Equal("42", env.GetValue("toString"));
        Assert.Equal(true, env.GetValue("toBool"));
    }

    [Fact]
    public void Run_CastFailures_ReturnNull()
    {
        var env = FishboneProgram.Run("""
let notANumber = "oops" as int;
let fromNull = null as int;
""", new FishboneConfiguration());

        Assert.Null(env.GetValue("notANumber"));
        Assert.Null(env.GetValue("fromNull"));
    }

    [Fact]
    public void Run_CastToRegisteredType_PassesInstanceThroughAndRejectsOthers()
    {
        var config = new FishboneConfiguration()
            .AddType<Widget>()
            .AddValue("known", new Widget())
            .AddValue("unrelated", "just a string");

        var env = FishboneProgram.Run("""
let same = known as Widget;
let mismatch = unrelated as Widget;
""", config);

        Assert.Same(config.Values["known"], env.GetValue("same"));
        Assert.Null(env.GetValue("mismatch"));
    }

    [Fact]
    public void Run_CastUsesRegisteredTypeConverter()
    {
        var config = new FishboneConfiguration()
            .AddType<Widget>()
            .AddTypeConverter(typeof(Widget), value => new Widget { Size = (int)value });

        var env = FishboneProgram.Run("""
let widget = 5 as Widget;
let size = widget.Size;
""", config);

        Assert.Equal(5, env.GetValue("size"));
    }

    [Fact]
    public void Run_CastToUnknownTypeName_RaisesRuntimeError()
    {
        var exception = Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("""let x = 1 as NoSuchType;""", new FishboneConfiguration()));
        Assert.Contains("not a type", exception.Message);
    }

    [Fact]
    public void Run_CastBindsTighterThanComparison()
    {
        var env = FishboneProgram.Run("""
let inRange = "5" as int < 10;
let sum = 1 + 2 as double;
""", new FishboneConfiguration());

        Assert.Equal(true, env.GetValue("inRange"));
        Assert.Equal(3.0, env.GetValue("sum"));
    }

    private sealed class Widget
    {
        public int Size { get; set; }
    }
}