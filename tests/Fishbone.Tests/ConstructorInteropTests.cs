namespace Fishbone.Tests;

public class ConstructorInteropTests
{
    [Fact]
    public void Run_RegisteredType_ConstructsAndExposesInstanceMembers()
    {
        var config = new FishboneConfiguration()
            .AddType<Point>();

        var env = FishboneProgram.Run("""
let p = Point(3, 4);
let x = p.X;
let y = p.Y;
let label = p.Label;
let sum = p.Sum();
""", config);

        Assert.Equal(3, env.GetValue("x"));
        Assert.Equal(4, env.GetValue("y"));
        Assert.Equal("xy", env.GetValue("label"));
        Assert.Equal(7, env.GetValue("sum"));
    }

    [Fact]
    public void Run_RegisteredType_SelectsConstructorOverloadByArgumentCount()
    {
        var config = new FishboneConfiguration()
            .AddType<Point>();

        var env = FishboneProgram.Run("""
let single = Point(5);
let label = single.Label;
let x = single.X;
let y = single.Y;
""", config);

        Assert.Equal("single", env.GetValue("label"));
        Assert.Equal(5, env.GetValue("x"));
        Assert.Equal(5, env.GetValue("y"));
    }

    [Fact]
    public void Run_RegisteredType_ConvertsConstructorArguments()
    {
        var config = new FishboneConfiguration()
            .AddType<Point>();

        var env = FishboneProgram.Run("""
let p = Point(3.0, 4.0);
let x = p.X;
""", config);

        Assert.Equal(3, env.GetValue("x"));
    }

    [Fact]
    public void Run_RegisteredType_HonorsCustomName()
    {
        var config = new FishboneConfiguration()
            .AddType<Point>("Vec");

        var env = FishboneProgram.Run("""
let v = Vec(1, 2);
let sum = v.Sum();
""", config);

        Assert.Equal(3, env.GetValue("sum"));

        // The default type name is not registered when a custom name is given.
        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let p = Point(1, 2);", config));
    }

    [Fact]
    public void Run_RegisteredType_WithNoMatchingConstructor_Throws()
    {
        var config = new FishboneConfiguration()
            .AddType<Point>();

        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let p = Point(1, 2, 3);", config));
    }

    [Fact]
    public void Run_RegisteredType_WithNoPublicConstructor_Throws()
    {
        var config = new FishboneConfiguration()
            .AddType<Hidden>();

        var exception = Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let h = Hidden();", config));
        Assert.Contains("constructor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_StructCalledWithNoArguments_ReturnsDefaultValue()
    {
        // like new Size() in c#. structs always have an empty constructor, but reflection
        // doesn't list it
        var config = new FishboneConfiguration()
            .AddType<Size>()
            .AddType<TimeSpan>();

        var env = FishboneProgram.Run("""
let empty = Size();
let sized = Size(2, 3);
let zero = TimeSpan();
""", config);

        Assert.Equal(default(Size), env.GetValue("empty"));
        Assert.Equal(new Size(2, 3), env.GetValue("sized"));
        Assert.Equal(TimeSpan.Zero, env.GetValue("zero"));
    }

    [Fact]
    public void Run_StructWithNoMatchingConstructor_Throws()
    {
        var config = new FishboneConfiguration().AddType<Size>();

        Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("let s = Size(1);", config));
    }

    private readonly record struct Size(int Width, int Height);

    private sealed class Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
            Label = "xy";
        }

        public Point(int value)
        {
            X = value;
            Y = value;
            Label = "single";
        }

        public int X { get; }
        public int Y { get; }
        public string Label { get; }

        public int Sum() => X + Y;
    }

    private sealed class Hidden
    {
        private Hidden()
        {
        }
    }
}