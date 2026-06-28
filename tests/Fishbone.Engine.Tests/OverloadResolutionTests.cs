namespace Fishbone.Engine.Tests;

public class OverloadResolutionTests
{
    [Fact]
    public void Run_OverloadedMethod_PrefersExactNumericTypeOverConvertible()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new OverloadSample());

        var env = FishboneEngine.Run("""
let fromInt = sample.Number(5);
let fromDouble = sample.Number(5.0);
""", config);

        // 5 is an int, so the int overload must win over the convertible double overload.
        Assert.Equal("int:5", env.GetValue("fromInt"));
        Assert.Equal("double:5", env.GetValue("fromDouble"));
    }

    [Fact]
    public void Run_OverloadedMethod_PrefersExactThenAssignableThenConvertible()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new OverloadSample());

        var env = FishboneEngine.Run("""
let exactString = sample.Describe("hello");
let assignableObject = sample.Describe(5);
""", config);

        // "hello" matches string exactly (beats object); 5 is assignable to object without a
        // conversion, which must beat the int->string Convert.ChangeType path.
        Assert.Equal("string:hello", env.GetValue("exactString"));
        Assert.Equal("object:5", env.GetValue("assignableObject"));
    }

    [Fact]
    public void Run_OverloadedMethod_WithEquallyGoodCandidates_ThrowsAmbiguous()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new OverloadSample());

        var exception = Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("""
let result = sample.Ambiguous(1, 2);
""", config));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_OverloadedMethod_SelectsByArgumentCount()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new OverloadSample());

        var env = FishboneEngine.Run("""
let none = sample.Greet();
let one = sample.Greet("there");
""", config);

        Assert.Equal("hi", env.GetValue("none"));
        Assert.Equal("hi:there", env.GetValue("one"));
    }

    private sealed class OverloadSample
    {
        public string Number(int value) => $"int:{value}";
        public string Number(double value) => $"double:{value}";

        public string Describe(string value) => $"string:{value}";
        public string Describe(object value) => $"object:{value}";

        public string Ambiguous(int a, double b) => $"a:{a},{b}";
        public string Ambiguous(double a, int b) => $"b:{a},{b}";

        public string Greet() => "hi";
        public string Greet(string name) => $"hi:{name}";
    }
}