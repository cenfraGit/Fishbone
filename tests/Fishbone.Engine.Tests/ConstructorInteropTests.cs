namespace Fishbone.Engine.Tests;

public class ConstructorInteropTests
{
    [Fact]
    public void Run_RegisteredType_ConstructsAndExposesInstanceMembers()
    {
        var config = new FishboneConfiguration()
            .RegisterType<Point>();

        var env = FishboneEngine.Run("""
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
            .RegisterType<Point>();

        var env = FishboneEngine.Run("""
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
            .RegisterType<Point>();

        var env = FishboneEngine.Run("""
let p = Point(3.0, 4.0);
let x = p.X;
""", config);

        Assert.Equal(3, env.GetValue("x"));
    }

    [Fact]
    public void Run_RegisteredType_HonorsCustomName()
    {
        var config = new FishboneConfiguration()
            .RegisterType<Point>("Vec");

        var env = FishboneEngine.Run("""
let v = Vec(1, 2);
let sum = v.Sum();
""", config);

        Assert.Equal(3, env.GetValue("sum"));

        // The default type name is not registered when a custom name is given.
        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("let p = Point(1, 2);", config));
    }

    [Fact]
    public void Run_RegisteredType_WithNoMatchingConstructor_Throws()
    {
        var config = new FishboneConfiguration()
            .RegisterType<Point>();

        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("let p = Point(1, 2, 3);", config));
    }

    [Fact]
    public void Run_RegisteredType_WithNoPublicConstructor_Throws()
    {
        var config = new FishboneConfiguration()
            .RegisterType<Hidden>();

        var exception = Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("let h = Hidden();", config));
        Assert.Contains("constructor", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

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