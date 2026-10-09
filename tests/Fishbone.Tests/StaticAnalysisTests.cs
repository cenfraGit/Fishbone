namespace Fishbone.Tests;

/// <summary>
/// <see cref="FishboneAnalysis"/> works out types without running the script. It only reports
/// what it's sure of: anything that could change at runtime is unknown, and unknown is silent.
/// </summary>
public class StaticAnalysisTests
{
    private sealed class Point
    {
        public int X { get; set; }
        public string Label = "";
        public Shape? Owner { get; set; }
        public Point Moved(int dx) => new() { X = X + dx };
        public static Point Origin { get; } = new();
    }

    // not sealed, so a value declared as Shape can be a derived type with more members
    private class Shape
    {
        public string Name { get; set; } = "";
    }

    // like HALCON's HTuple, which a converter turns into a plain value on the way back
    private sealed class Wrapped
    {
        public int Value { get; set; }
    }

    private static FishboneDescription Description() => new FishboneConfiguration()
        .AddType<Point>()
        .AddValue("origin", new Point())
        .AddBuiltIn("makeShape", new Func<Shape>(() => new Shape()))
        .AddBuiltIn("wrap", new Func<int, Wrapped>(value => new Wrapped { Value = value }))
        .AddTypeConverter(typeof(Wrapped), value => value, value => ((Wrapped)value).Value)
        .Describe();

    private static FishboneAnalysis Analyze(string code) => FishboneAnalysis.Analyze(code, Description());

    private static Type? TypeAt(string code, string expression, int line, int column = 1) =>
        Analyze(code).TypeOf(expression, line, column)?.Type;

    [Theory]
    [InlineData("1", typeof(int))]
    [InlineData("3000000000", typeof(long))]
    [InlineData("2.5", typeof(double))]
    [InlineData("\"text\"", typeof(string))]
    [InlineData("$\"x is {1}\"", typeof(string))]
    [InlineData("true", typeof(bool))]
    [InlineData("[1, 2]", typeof(List<object?>))]
    [InlineData("{:}", typeof(Dictionary<object, object?>))]
    [InlineData("{1: 2}", typeof(Dictionary<object, object?>))]
    public void Literals_HaveTheirRuntimeType(string expression, Type expected)
    {
        Assert.Equal(expected, TypeAt("", expression, 1));
    }

    [Theory]
    [InlineData("1 + 2", typeof(int))]
    [InlineData("1 + 3000000000", typeof(long))]
    [InlineData("1 * 2.5", typeof(double))]
    [InlineData("4 / 2", typeof(double))]
    [InlineData("5 % 2", typeof(int))]
    [InlineData("\"a\" + 1", typeof(string))]
    [InlineData("1 + \"a\"", typeof(string))]
    [InlineData("-3000000000", typeof(long))]
    [InlineData("1 < 2", typeof(bool))]
    [InlineData("not 1", typeof(bool))]
    [InlineData("1 as long", typeof(long))]
    [InlineData("[1] as int[]", typeof(int[]))]
    public void Operators_FollowTheInterpretersNumberRules(string expression, Type expected)
    {
        Assert.Equal(expected, TypeAt("", expression, 1));
    }

    [Fact]
    public void UnknownOperands_GiveAnUnknownType()
    {
        Assert.Null(TypeAt("", "true + 1", 1));
        Assert.Null(TypeAt("", "nothing * 2", 1));
    }

    [Fact]
    public void CallingARegisteredType_ConstructsIt()
    {
        var type = Analyze("").TypeOf("Point", 1, 1);
        Assert.Equal(new FishboneExpressionType(typeof(Point), IsStatic: true), type);
        Assert.Equal(typeof(Point), TypeAt("", "Point()", 1));
        Assert.Equal(typeof(int), TypeAt("", "int", 1));
    }

    [Fact]
    public void Members_FollowPropertyFieldAndReturnTypes()
    {
        Assert.Equal(typeof(int), TypeAt("", "Point().X", 1));
        Assert.Equal(typeof(string), TypeAt("", "Point().Label", 1));
        Assert.Equal(typeof(Point), TypeAt("", "Point().Moved(1)", 1));
        Assert.Equal(typeof(Point), TypeAt("", "Point.Origin", 1));
        Assert.Equal(typeof(string), TypeAt("", "origin.ToString()", 1));
        Assert.Equal(typeof(Shape), TypeAt("", "makeShape()", 1));
    }

    [Fact]
    public void ConvertedReturnTypes_AreUnknown()
    {
        Assert.Null(TypeAt("", "wrap(1)", 1));
    }

    [Fact]
    public void Variable_DeclaredOnce_HasItsValuesType()
    {
        const string code = """
            let p = Point();
            let d = {:};
            let n = p.X + 1;

            """;
        Assert.Equal(typeof(Point), TypeAt(code, "p", 4));
        Assert.Equal(typeof(Dictionary<object, object?>), TypeAt(code, "d", 4));
        Assert.Equal(typeof(int), TypeAt(code, "n", 4));
    }

    [Fact]
    public void Variable_AssignedAgain_IsUnknown()
    {
        Assert.Null(TypeAt("let p = Point();\np = 5;\n", "p", 3));
        Assert.Null(TypeAt("let n = 1;\nn += 1;\n", "n", 3));
        Assert.Null(TypeAt("origin = 1;\n", "origin", 2));
        Assert.Equal(typeof(Point), TypeAt("", "origin", 1));
    }

    [Fact]
    public void Variable_PassedAsOut_IsUnknown()
    {
        Assert.Null(TypeAt("let p = Point();\nsomething(out p);\n", "p", 3));
    }

    [Fact]
    public void Variable_IsOnlyVisibleInsideItsBlock()
    {
        const string code = """
            {
                let inner = 1;

            }

            """;
        Assert.Equal(typeof(int), TypeAt(code, "inner", 3));
        Assert.Null(TypeAt(code, "inner", 5));
        Assert.Contains(Analyze(code).VisibleAt(3, 1), variable => variable.Name == "inner");
        Assert.DoesNotContain(Analyze(code).VisibleAt(5, 1), variable => variable.Name == "inner");
    }

    [Fact]
    public void Declaration_DoesNotSeeItselfInItsValue()
    {
        // the value is evaluated before the inner x exists, so it reads the outer one
        const string code = """
            let x = Point();
            {
                let x = x.X;

            }
            """;
        Assert.Equal(typeof(int), TypeAt(code, "x", 4));
    }

    [Fact]
    public void Function_CanSeeLaterOuterDeclarations_SoTheyAreUnknownInside()
    {
        const string code = """
            func f(a)
            {

            }
            let q = 1;
            """;
        Assert.Null(TypeAt(code, "q", 3));
        Assert.Null(TypeAt(code, "a", 3));
        Assert.Contains(Analyze(code).VisibleAt(3, 1), variable => variable.Name == "a");
    }

    [Fact]
    public void VisibleAt_GivesAScriptFunctionsParameters()
    {
        const string code = """
            func area(width, height) { return width * height; }

            """;
        var function = Assert.Single(Analyze(code).VisibleAt(2, 1));

        Assert.Equal(["width", "height"], function.Parameters);
        Assert.Null(Assert.Single(Analyze("let x = 1;\n").VisibleAt(2, 1)).Parameters);
    }

    [Theory]
    [InlineData("0, 10", typeof(int))]
    [InlineData("0, 3000000000", typeof(long))]
    [InlineData("0, 1, 0.5", typeof(double))]
    public void ForVariable_TakesItsTypeFromTheBounds(string bounds, Type expected)
    {
        string code = $$"""
            for (i in {{bounds}})
            {

            }
            """;
        Assert.Equal(expected, TypeAt(code, "i", 3));
    }

    [Fact]
    public void CatchVariable_IsAnException()
    {
        const string code = """
            try { } catch (e)
            {

            }
            """;
        Assert.Equal(typeof(Exception), TypeAt(code, "e", 3));
    }

    [Fact]
    public void MissingMember_OnAKnownType_IsReported()
    {
        const string code = """
            let p = Point();
            println(p.Nope);
            """;
        var diagnostic = Assert.Single(Analyze(code).Diagnostics);

        Assert.Equal((2, 9, 2, 15), (diagnostic.Line, diagnostic.Column, diagnostic.EndLine, diagnostic.EndColumn));
        Assert.Equal("Type 'Point' does not have a public member named 'Nope'.", diagnostic.Message);
    }

    [Fact]
    public void MissingStaticMember_IsReported()
    {
        Assert.Single(Analyze("Point.Nope;").Diagnostics);
    }

    [Theory]
    [InlineData("func f(x) { return x.Nope; }")]
    [InlineData("let s = makeShape(); s.Nope;")]
    [InlineData("Point().Owner.Nope;")]
    [InlineData("let p = Point(); p = 1; p.Nope;")]
    [InlineData("let p = Point(); p.X; p.Moved(1).Label;")]
    public void UncertainOrValidMembers_AreNotReported(string code)
    {
        // a Shape can be a derived type with more members, so only sealed or exact types are checked
        Assert.Empty(Analyze(code).Diagnostics);
    }

    [Fact]
    public void SyntaxErrors_AreDiagnostics()
    {
        var analysis = Analyze("let = 1;");

        Assert.False(analysis.Parsed);
        var diagnostic = Assert.Single(analysis.Diagnostics);
        Assert.Equal(1, diagnostic.Line);
    }

    [Fact]
    public void TypeOf_WorksWithoutAParse_ForConfigurationNames()
    {
        // the IDE asks while the user is typing, when the script usually doesn't parse
        Assert.Equal(typeof(Point), Analyze("let = ").TypeOf("Point()", 1, 1)?.Type);
        Assert.Null(Analyze("").TypeOf("p.(", 1, 1));
    }
}
