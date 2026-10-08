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
    public void Run_CastListToArray_ReturnsTypedArray()
    {
        var config = new FishboneConfiguration().AddType<DayOfWeek>();

        var env = FishboneProgram.Run("""
let ints = [1, 2, 3] as int[];
let doubles = [1, 2.5] as double[];
let days = [1, 2] as DayOfWeek[];
let empty = [] as string[];
let nested = [[1, 2], [3]] as int[][];
let second = ([1, 2] as int[])[1];
""", config);

        Assert.Equal(new[] { 1, 2, 3 }, Assert.IsType<int[]>(env.GetValue("ints")));
        Assert.Equal(new[] { 1.0, 2.5 }, Assert.IsType<double[]>(env.GetValue("doubles")));
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday }, Assert.IsType<DayOfWeek[]>(env.GetValue("days")));
        Assert.Empty(Assert.IsType<string[]>(env.GetValue("empty")));
        var nested = Assert.IsType<int[][]>(env.GetValue("nested"));
        Assert.Equal(new[] { 1, 2 }, nested[0]);
        Assert.Equal(new[] { 3 }, nested[1]);
        Assert.Equal(2, env.GetValue("second"));
    }

    [Theory]
    [InlineData("let r = [1, 2.5] as int[];")]
    [InlineData("""let r = [1, "2"] as int[];""")]
    [InlineData("let r = 5 as int[];")]
    [InlineData("""let r = "abc" as char[];""")]
    public void Run_CastToArrayThatDoesNotFit_ReturnsNull(string code)
    {
        // like other casts, a failed cast is null. elements follow the argument rules, so
        // 2.5 doesn't truncate into an int[]
        var env = FishboneProgram.Run(code, new FishboneConfiguration());

        Assert.Null(env.GetValue("r"));
    }

    [Fact]
    public void Run_CastArrayToSameArrayType_ReturnsSameInstance()
    {
        var values = new[] { 1, 2 };
        var config = new FishboneConfiguration().AddValue("values", values);

        var env = FishboneProgram.Run("let r = values as int[];", config);

        Assert.Same(values, env.GetValue("r"));
    }

    [Fact]
    public void Run_CastToArrayOfUnknownType_RaisesError()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let r = [1] as Missing[];", new FishboneConfiguration()));

        Assert.Contains("Missing[]", exception.Message);
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
    public void Run_CastToKeywordTypes_FollowsCSharpCasts()
    {
        var env = FishboneProgram.Run("""
let toLong = 3000000000 as long;
let truncatedLong = 2.7 as long;
let toFloat = 2.5 as float;
let toByte = 255 as byte;
let toDecimal = 2.5 as decimal;
let toChar = 65 as char;
let toObject = 5 as object;
""", new FishboneConfiguration());

        Assert.Equal(3000000000L, env.GetValue("toLong"));
        Assert.Equal(2L, env.GetValue("truncatedLong"));
        Assert.Equal(2.5f, env.GetValue("toFloat"));
        Assert.Equal((byte)255, env.GetValue("toByte"));
        Assert.Equal(2.5m, env.GetValue("toDecimal"));
        Assert.Equal('A', env.GetValue("toChar"));
        Assert.Equal(5, env.GetValue("toObject"));
    }

    [Fact]
    public void Run_CastDoubleTooBigForFloat_ReturnsInfinity()
    {
        // like a c# checked cast: float overflow isn't an error
        var env = FishboneProgram.Run("""
let tooBig = 400000000000000000000000000000000000000.0 as float;
""", new FishboneConfiguration());

        Assert.Equal(float.PositiveInfinity, env.GetValue("tooBig"));
    }

    [Fact]
    public void Run_CastToKeywordTypes_OutOfRangeOrNotACast_ReturnsNull()
    {
        var env = FishboneProgram.Run("""
let byteTooBig = 256 as byte;
let negativeUint = -1 as uint;
let stringToChar = "A" as char;
""", new FishboneConfiguration());

        Assert.Null(env.GetValue("byteTooBig"));
        Assert.Null(env.GetValue("negativeUint"));
        Assert.Null(env.GetValue("stringToChar"));
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