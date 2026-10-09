namespace Fishbone.Tests;

/// <summary>
/// Implicit argument conversion when a script calls .NET: a value converts only when no
/// information is lost, otherwise the call fails with an error naming the parameter.
/// </summary>
public class ArgumentConversionTests
{
    private static FishboneConfiguration Config() => new FishboneConfiguration()
        .AddBuiltIn("takeInt", new Func<int, int>(value => value))
        .AddBuiltIn("takeLong", new Func<long, long>(value => value))
        .AddBuiltIn("takeDouble", new Func<double, double>(value => value))
        .AddBuiltIn("takeFloat", new Func<float, float>(value => value))
        .AddBuiltIn("takeNullableInt", new Func<int?, string>(value => value?.ToString() ?? "none"))
        .AddBuiltIn("takeString", new Func<string?, string>(value => value ?? "none"))
        .AddBuiltIn("takeDay", new Func<DayOfWeek, DayOfWeek>(value => value))
        .AddBuiltIn("sample", new Sample());

    [Fact]
    public void Run_LosslessArguments_Convert()
    {
        var env = FishboneProgram.Run("""
let fromInt = takeInt(2);
let fromWholeDouble = takeInt(2.0);
let fromDivision = takeInt(4 / 2);
let intToLong = takeLong(2);
let intToDouble = takeDouble(2);
let nullToNullable = takeNullableInt(null);
let nullToString = takeString(null);
let intToEnum = takeDay(1);
let doubleToFloat = takeFloat(0.1);
let intToFloat = takeFloat(2);
""", Config());

        Assert.Equal(2, env.GetValue("fromInt"));
        Assert.Equal(2, env.GetValue("fromWholeDouble"));
        Assert.Equal(2, env.GetValue("fromDivision"));
        Assert.Equal(2L, env.GetValue("intToLong"));
        Assert.Equal(2.0, env.GetValue("intToDouble"));
        Assert.Equal("none", env.GetValue("nullToNullable"));
        Assert.Equal("none", env.GetValue("nullToString"));
        Assert.Equal(DayOfWeek.Monday, env.GetValue("intToEnum"));
        // double to float is allowed when in range, even though it loses precision
        Assert.Equal(0.1f, env.GetValue("doubleToFloat"));
        Assert.Equal(2f, env.GetValue("intToFloat"));
    }

    [Theory]
    [InlineData("takeInt(0.5)", "Int32")]
    [InlineData("takeInt(1.5)", "Int32")]
    [InlineData("takeInt(2.5)", "Int32")]
    [InlineData("takeInt(-2.7)", "Int32")]
    [InlineData("takeInt(null)", "Int32")]
    [InlineData("takeInt(\"0\")", "Int32")]
    [InlineData("takeInt(true)", "Int32")]
    [InlineData("takeInt(3000000000)", "Int32")]
    [InlineData("takeInt(3000000000.0)", "Int32")]
    [InlineData("takeDouble(\"3.5\")", "Double")]
    [InlineData("takeFloat(400000000000000000000000000000000000000.0)", "Single")]
    [InlineData("takeLong(null)", "Int64")]
    [InlineData("takeDay(\"Monday\")", "DayOfWeek")]
    [InlineData("takeDay(1.5)", "DayOfWeek")]
    public void Run_LossyArgumentToDelegate_RaisesError(string call, string parameterType)
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run($"let result = {call};", Config()));

        Assert.Contains($"not compatible with parameter 'value' of type '{parameterType}'", exception.Message);
    }

    [Fact]
    public void Run_LossyArgumentToMethod_RaisesError()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let result = sample.Read(1.5);", Config()));

        Assert.Contains("'index'", exception.Message);
    }

    [Fact]
    public void Run_LossyArgumentToConstructor_RaisesError()
    {
        var config = new FishboneConfiguration().AddType<Sample>();

        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let s = Sample(0.5);", config));

        Assert.Contains("'size'", exception.Message);
    }

    [Fact]
    public void Run_LossyArgument_DoesNotFallBackToOtherOverload()
    {
        // 2.5 can't go to Pick(int) and a number never becomes a string, so neither
        // overload applies. it's a conversion error, not an ambiguous call
        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("let result = sample.Pick(2.5);", Config()));

        Assert.DoesNotContain("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Sample
    {
        public Sample() { }
        public Sample(int size) => Size = size;

        public int Size { get; }

        public int Read(int index) => index;

        public string Pick(int value) => $"int:{value}";
        public string Pick(string value) => $"string:{value}";
    }
}