namespace Fishbone.Tests;

public class CastExpressionTests
{
    [Fact]
    public void Run_NumericCasts_FollowCSharpCasts()
    {
        var env = FishboneProgram.Run("""
let whole = 2.0 as int;
let truncated = 2.7 as int;
let half = 2.5 as int;
let negative = -2.7 as int;
let widened = 2 as double;
""", new FishboneConfiguration());

        Assert.Equal(2, env.GetValue("whole"));
        Assert.Equal(2, env.GetValue("truncated"));
        Assert.Equal(2, env.GetValue("half"));
        Assert.Equal(-2, env.GetValue("negative"));
        Assert.Equal(2.0, env.GetValue("widened"));
    }

    [Fact]
    public void Run_CastIntToEnum_ReturnsEnumValue()
    {
        var config = new FishboneConfiguration().AddType<DayOfWeek>();

        var env = FishboneProgram.Run("let day = 1 as DayOfWeek;", config);

        Assert.Equal(DayOfWeek.Monday, env.GetValue("day"));
    }

    [Fact]
    public void Run_CastOutOfRange_ReturnsNull()
    {
        var env = FishboneProgram.Run("""
let tooBig = 3000000000 as int;
let tooBigDouble = 3000000000.0 as int;
""", new FishboneConfiguration());

        Assert.Null(env.GetValue("tooBig"));
        Assert.Null(env.GetValue("tooBigDouble"));
    }

    [Fact]
    public void Run_CastsThatAreNotCSharpCasts_ReturnNull()
    {
        var config = new FishboneConfiguration().AddType<DayOfWeek>();

        var env = FishboneProgram.Run("""
let fromString = "42" as int;
let fromDoubleString = "3.5" as double;
let numberToString = 42 as string;
let stringToBool = "true" as bool;
let boolToInt = true as int;
let enumFromName = "Monday" as DayOfWeek;
""", config);

        Assert.Null(env.GetValue("fromString"));
        Assert.Null(env.GetValue("fromDoubleString"));
        Assert.Null(env.GetValue("numberToString"));
        Assert.Null(env.GetValue("stringToBool"));
        Assert.Null(env.GetValue("boolToInt"));
        Assert.Null(env.GetValue("enumFromName"));
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
    public void Run_CastInClassHierarchy_UpcastsAndChecksDowncasts()
    {
        var config = new FishboneConfiguration()
            .AddType<Animal>()
            .AddType<Dog>()
            .AddValue("dog", new Dog())
            .AddValue("cat", new Cat());

        var env = FishboneProgram.Run("""
let upcast = dog as Animal;
let downcast = upcast as Dog;
let wrongDowncast = cat as Dog;
let stillBarks = (dog as Animal).Bark();
""", config);

        Assert.Same(config.Values["dog"], env.GetValue("upcast"));
        Assert.Same(config.Values["dog"], env.GetValue("downcast"));
        Assert.Null(env.GetValue("wrongDowncast"));
        // no static types: an upcast doesn't hide members of the real type
        Assert.Equal("woof", env.GetValue("stillBarks"));
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
let inRange = 5.5 as int < 10;
let sum = 1 + 2 as double;
""", new FishboneConfiguration());

        Assert.Equal(true, env.GetValue("inRange"));
        Assert.Equal(3.0, env.GetValue("sum"));
    }

    private sealed class Widget
    {
        public int Size { get; set; }
    }

    private class Animal { }

    private sealed class Dog : Animal
    {
        public string Bark() => "woof";
    }

    private sealed class Cat : Animal { }
}