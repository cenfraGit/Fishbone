namespace Fishbone.Engine.Tests;

/// <summary>
/// Covers <see cref="FishboneConfiguration.AddTypeConverter"/>: a host can teach the interop path how
/// to convert a .NET type the generic path cannot handle (one that is neither IConvertible nor an
/// enum). This is the mechanism the Halcon bridge uses for HTuple, exercised here with a plain
/// wrapper type so the behaviour is verified independently of Halcon.
/// </summary>
public class TypeConverterTests
{
    // a wrapper that is deliberately not IConvertible, like Halcon's HTuple
    private sealed class Boxed
    {
        public Boxed(int value) => Value = value;
        public int Value { get; }
    }

    private delegate void ProduceDelegate(int seed, out Boxed result);

    private static FishboneConfiguration ConfigWithBoxedConverter() =>
        new FishboneConfiguration()
            .AddTypeConverter(
                typeof(Boxed),
                toNet: value => new Boxed(Convert.ToInt32(value)),
                fromNet: value => ((Boxed)value).Value);

    [Fact]
    public void ToNet_ConvertsScriptValue_ForByValueParameter()
    {
        // 'unwrap' takes a Boxed by value; passing the script int 21 must convert via the registry
        var config = ConfigWithBoxedConverter()
            .AddFunction("unwrap", new Func<Boxed, int>(box => box.Value * 2));

        var env = FishboneEngine.Run("let result = unwrap(21);", config);

        Assert.Equal(42, env.GetValue("result"));
    }

    [Fact]
    public void FromNet_NormalizesOutValue_BackIntoScript()
    {
        // 'produce' writes a Boxed through an out parameter; the registry's from-direction must turn
        // it back into a plain script value at the write-back boundary
        var config = ConfigWithBoxedConverter()
            .AddFunction("produce", new ProduceDelegate((int seed, out Boxed result) => result = new Boxed(seed + 1)));

        var env = FishboneEngine.Run("produce(41, out made);", config);

        Assert.Equal(42, env.GetValue("made"));
    }

    [Fact]
    public void Converter_RoundTrips_ThroughBothDirections()
    {
        var config = ConfigWithBoxedConverter()
            .AddFunction("produce", new ProduceDelegate((int seed, out Boxed result) => result = new Boxed(seed)))
            .AddFunction("unwrap", new Func<Boxed, int>(box => box.Value));

        // produce(10, out b) -> b is the script int 10 (from-direction); unwrap(b) converts it back
        // to a Boxed (to-direction) and reads it out again
        var env = FishboneEngine.Run("""
produce(10, out b);
let result = unwrap(b);
""", config);

        Assert.Equal(10, env.GetValue("b"));
        Assert.Equal(10, env.GetValue("result"));
    }

    [Fact]
    public void WithoutConverter_NonConvertibleType_IsRejected()
    {
        // same call, but no converter registered: the generic path cannot turn an int into a Boxed
        var config = new FishboneConfiguration()
            .AddFunction("unwrap", new Func<Boxed, int>(box => box.Value));

        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("let result = unwrap(21);", config));
    }
}