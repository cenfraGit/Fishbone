namespace Fishbone.Tests;

public class MemberAssignmentTests
{
    private static (FishboneConfiguration Config, Box Box) Setup()
    {
        var box = new Box();
        var config = new FishboneConfiguration()
            .AddType(typeof(Counter))
            .AddValue("box", box)
            .AddValue("spot", new Location())
            .AddValue("spots", new[] { new Location() })
            .AddBuiltIn("getSpot", new Func<Location>(() => box.Spot));
        return (config, box);
    }

    [Fact]
    public void Run_AssignToPropertyAndField_SetsValue()
    {
        var (config, box) = Setup();

        FishboneProgram.Run("""
box.Count = 5;
box.Label = "hi";
""", config);

        Assert.Equal(5, box.Count);
        Assert.Equal("hi", box.Label);
    }

    [Fact]
    public void Run_AssignedValue_FollowsArgumentConversionRules()
    {
        var (config, box) = Setup();

        FishboneProgram.Run("box.Count = 2.0;", config);
        Assert.Equal(2, box.Count);

        var fromDouble = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("box.Count = 2.5;", config));
        Assert.Equal("Cannot assign a value of type 'Double' to 'Count' of type 'Int32'.", fromDouble.Message);
        var fromString = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("box.Count = \"5\";", config));
        Assert.Equal("Cannot assign a value of type 'String' to 'Count' of type 'Int32'.", fromString.Message);
        Assert.Equal(2, box.Count);
    }

    [Fact]
    public void Run_CompoundAssignToMember_UpdatesValue()
    {
        var (config, box) = Setup();

        FishboneProgram.Run("""
box.Count = 1;
box.Count += 2;
box.Label = "hi";
box.Label += "!";
""", config);

        Assert.Equal(3, box.Count);
        Assert.Equal("hi!", box.Label);
    }

    [Fact]
    public void Run_AssignThroughChain_SetsMemberOnInnerObject()
    {
        var (config, box) = Setup();

        FishboneProgram.Run("box.Inner.Count = 7;", config);

        Assert.Equal(7, box.Inner.Count);
    }

    [Fact]
    public void Run_AssignToStaticMembers_SetsValue()
    {
        var (config, _) = Setup();

        FishboneProgram.Run("""
Counter.Value = 4;
Counter.Value += 1;
Counter.Total = 10;
""", config);

        Assert.Equal(5, Counter.Value);
        Assert.Equal(10, Counter.Total);
    }

    [Theory]
    [InlineData("box.ReadOnlyValue = 1;", "ReadOnlyValue")]
    [InlineData("box.ReadOnlyField = 1;", "ReadOnlyField")]
    [InlineData("box.PrivateSet = 1;", "PrivateSet")]
    [InlineData("box.Method = 1;", "Method")]
    [InlineData("box.Missing = 1;", "Missing")]
    [InlineData("Counter.Constant = 1;", "Constant")]
    public void Run_AssignToMemberThatCannotBeSet_RaisesErrorNamingMember(string code, string member)
    {
        var (config, _) = Setup();

        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(code, config));

        Assert.Contains($"'{member}'", exception.Message);
    }

    [Fact]
    public void Run_AssignToMemberOfNull_RaisesError()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("""
let nothing = null;
nothing.Count = 1;
""", new FishboneConfiguration()));

        Assert.Contains("null", exception.Message);
    }

    [Fact]
    public void Run_MemberAccessDisabled_MemberAssignmentIsRuntimeError()
    {
        var (config, _) = Setup();
        config.EnableMemberAccess = false;

        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("box.Count = 5;", config));

        Assert.Contains("disabled by the host configuration", exception.Message);
    }

    [Fact]
    public void Run_AssignToStructVariable_SetsValue()
    {
        var (config, _) = Setup();

        var env = FishboneProgram.Run("""
spot.X = 5;
spot.Y = 6;
spot.X += 1;
""", config);

        var spot = Assert.IsType<Location>(env.GetValue("spot"));
        Assert.Equal(6, spot.X);
        Assert.Equal(6, spot.Y);
    }

    [Theory]
    [InlineData("box.Spot.X = 5;")]
    [InlineData("box.Spot.Y = 5;")]
    [InlineData("getSpot().X = 5;")]
    [InlineData("spots[0].X = 5;")]
    public void Run_AssignToMemberOfStructCopy_RaisesError(string code)
    {
        // like C# (CS1612): these read a copy of the struct, so the assignment would be lost.
        // it's a runtime error, since the parser can't know the value is a struct
        var (config, box) = Setup();

        Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(code, config));

        Assert.Equal(0, box.Spot.X);
        Assert.Equal(0, box.Spot.Y);
    }

    private sealed class Box
    {
        public int Count { get; set; }
        public string Label = "";
        public int ReadOnlyValue { get; } = 1;
        public readonly int ReadOnlyField = 2;
        public int PrivateSet { get; private set; }
        public InnerBox Inner { get; } = new();
        public Location Spot { get; set; }

        public void Method() { }
    }

    private sealed class InnerBox
    {
        public int Count { get; set; }
    }

    private struct Location
    {
        public int X;
        public int Y { get; set; }
    }

    private static class Counter
    {
        public const int Constant = 3;
        public static int Value;
        public static int Total { get; set; }
    }
}