namespace Fishbone.Tests;

public class ReflectionMemberAccessTests
{
    [Fact]
    public void Run_MemberAccess_ReadsPublicFieldsPropertiesAndChains()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new ReflectionSample());

        var env = FishboneProgram.Run("""
let fieldValue = sample.PublicField;
let propertyValue = sample.Name;
let childName = sample.Child.Name;
let itemName = sample.Items[0].Name;
""", config);

        Assert.Equal(7, env.GetValue("fieldValue"));
        Assert.Equal("root", env.GetValue("propertyValue"));
        Assert.Equal("child", env.GetValue("childName"));
        Assert.Equal("first", env.GetValue("itemName"));
    }

    [Fact]
    public void Run_MethodCalls_InvokePublicInstanceMethods()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new ReflectionSample());

        var env = FishboneProgram.Run("""
let description = sample.Describe();
let size = sample.Resize(3, 4);
let childName = sample.GetChild().Name;
let overloadedZero = sample.Overloaded();
let overloadedOne = sample.Overloaded("x");
let scaled = sample.Scale(2);
""", config);

        Assert.Equal("sample:root", env.GetValue("description"));
        Assert.Equal("3x4", env.GetValue("size"));
        Assert.Equal("child", env.GetValue("childName"));
        Assert.Equal("zero", env.GetValue("overloadedZero"));
        Assert.Equal("one:x", env.GetValue("overloadedOne"));
        Assert.Equal(5.0, env.GetValue("scaled"));
    }

    [Fact]
    public void Run_MethodCalls_AssignOutAndRefIdentifierArguments()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new ReflectionSample());

        var env = FishboneProgram.Run("""
let ok = sample.TryGetNumber("answer", out result);
let value = 10;
sample.Increment(ref value);
""", config);

        Assert.Equal(true, env.GetValue("ok"));
        Assert.Equal(42, env.GetValue("result"));
        Assert.Equal(11, env.GetValue("value"));
    }

    [Fact]
    public void Run_MethodCalls_WithInvalidAccessOrArguments_ThrowClearErrors()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("sample", new ReflectionSample());

        AssertError("let value = sample.Missing;", "Type 'ReflectionSample' does not have a public member named 'Missing'.");
        AssertError("let value = null; let name = value.Name;", "Cannot access member 'Name' on null.");
        AssertError("let value = sample.Resize(1);", "No argument supplied for parameter 'height', which has no default value.");
        AssertError("let value = sample.Name();", "Object of type 'String' is not callable.");
        AssertError("""let value = sample.Resize("wide", "high");""", "Argument 1 of type 'String' is not compatible with parameter 'width' of type 'Int32'.");
        // out parameter called without the 'out' keyword
        AssertError("""let number = 0; let ok = sample.TryGetNumber("answer", number);""", "Parameter 'value' is an out parameter; pass the argument with 'out'.");
        // 'out' given, but the target is not a plain variable
        AssertError("""let value = 0; let ok = sample.TryGetNumber("answer", out [value][0]);""", "Out argument 'value' must be a variable.");

        void AssertError(string code, string message)
        {
            var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(code, config));
            Assert.Equal(message, exception.Message);
        }
    }

    private sealed class ReflectionSample
    {
        public int PublicField = 7;
        public string Name { get; } = "root";
        public ReflectionChild Child { get; } = new("child");
        public List<ReflectionChild> Items { get; } = [new("first")];

        public string Describe() => $"sample:{Name}";
        public string Resize(int width, int height) => $"{width}x{height}";
        public ReflectionChild GetChild() => Child;
        public string Overloaded() => "zero";
        public string Overloaded(string value) => $"one:{value}";
        public double Scale(double value) => value * 2.5;

        public bool TryGetNumber(string key, out int value)
        {
            value = key == "answer" ? 42 : 0;
            return key == "answer";
        }

        public void Increment(ref int value)
        {
            value++;
        }
    }

    private sealed class ReflectionChild
    {
        public ReflectionChild(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}