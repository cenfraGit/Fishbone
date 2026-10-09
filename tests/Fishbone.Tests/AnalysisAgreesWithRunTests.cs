namespace Fishbone.Tests;

/// <summary>
/// The analyzer's one firm rule is never to report something that runs fine, so its answers are
/// checked against what the interpreter actually does with the same configuration.
/// </summary>
public class AnalysisAgreesWithRunTests
{
    public sealed class Point
    {
        public int X { get; set; } = 3;
        public string Label = "p";
        public Point Moved(int dx) => new() { X = X + dx };
        public static Point Origin { get; } = new();
    }

    private static FishboneConfiguration Config() => new FishboneConfiguration()
        .AddType<Point>()
        .AddValue("origin", new Point())
        // a registered type can come in as a value too, and then it's still a type
        .AddValue("P", new RegisteredType(typeof(Point)));

    [Theory]
    [InlineData("1")]
    [InlineData("3000000000")]
    [InlineData("2.5")]
    [InlineData("\"text\"")]
    [InlineData("$\"x is {1}\"")]
    [InlineData("true")]
    [InlineData("[1, 2]")]
    [InlineData("{1: 2}")]
    [InlineData("1 + 2")]
    [InlineData("1 + 3000000000")]
    [InlineData("1 * 2.5")]
    [InlineData("4 / 2")]
    [InlineData("5 % 2")]
    [InlineData("\"a\" + 1")]
    [InlineData("1 + \"a\"")]
    [InlineData("-3000000000")]
    [InlineData("1 < 2")]
    [InlineData("not 1")]
    [InlineData("1 as long")]
    [InlineData("[1] as int[]")]
    [InlineData("Point()")]
    [InlineData("Point().X")]
    [InlineData("Point().Label")]
    [InlineData("Point().Moved(1)")]
    [InlineData("Point.Origin")]
    [InlineData("origin.ToString()")]
    public void KnownType_IsTheTypeTheRunProduces(string expression)
    {
        var config = Config();
        Type? analyzed = FishboneAnalysis.Analyze("", config.Describe()).TypeOf(expression, 1, 1)?.Type;

        object value = FishboneProgram.Run($"let v = {expression};", config).GetValue("v");

        Assert.NotNull(analyzed);
        Assert.Equal(value.GetType(), analyzed);
    }

    [Theory]
    [InlineData("let p = Point(); let a = p.X; let b = p.Moved(1).Label;")]
    [InlineData("let a = P.Origin; let b = P().X;")]
    [InlineData("let a = origin.Label;")]
    public void ScriptThatRuns_HasNoDiagnostics(string code)
    {
        var config = Config();
        FishboneProgram.Run(code, config);

        Assert.Empty(FishboneAnalysis.Analyze(code, config.Describe()).Diagnostics);
    }

    [Theory]
    [InlineData("let p = Point();\np.Nope;")]
    [InlineData("Point.Nope;")]
    [InlineData("origin.Nope;")]
    public void ReportedMember_FailsWhenRun(string code)
    {
        var config = Config();
        var diagnostic = Assert.Single(FishboneAnalysis.Analyze(code, config.Describe()).Diagnostics);

        var error = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(code, config));

        Assert.Contains("Nope", diagnostic.Message);
        Assert.Contains("Nope", error.Message);
        Assert.Equal(diagnostic.Line, error.Line);
    }
}
