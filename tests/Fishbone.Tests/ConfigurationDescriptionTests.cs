namespace Fishbone.Tests;

/// <summary>
/// <see cref="FishboneConfiguration.Describe"/> lists every name a configuration puts into scripts,
/// with its kind, type and signatures, and the members a script can reach with <c>.</c>. The
/// members match what the interpreter resolves.
/// </summary>
public class ConfigurationDescriptionTests
{
    private static FishboneConfiguration Config() => new FishboneConfiguration()
        .AddBuiltIn("add", new Func<int, int, int>((first, second) => first + second))
        .AddBuiltIn("abs", new BoundMethod(null, typeof(Math).GetMethods().Where(m => m.Name == "Abs").ToList()))
        .AddBuiltIn("tryParse", new BoundMethod(null, [typeof(ConfigurationDescriptionTests).GetMethod(nameof(TryParse))!]))
        .AddBuiltIn("lookup", new LookupCallable())
        .AddBuiltIn("PI", 3.14)
        .AddValue("origin", new Point(0, 0))
        .AddType<Point>();

    private static FishboneSymbol Symbol(string name) =>
        Assert.Single(Config().Describe().Symbols, symbol => symbol.Name == name);

    [Theory]
    [InlineData("add", FishboneSymbolKind.Function)]
    [InlineData("abs", FishboneSymbolKind.Function)]
    [InlineData("lookup", FishboneSymbolKind.Function)]
    [InlineData("PI", FishboneSymbolKind.Constant)]
    [InlineData("origin", FishboneSymbolKind.Value)]
    [InlineData("Point", FishboneSymbolKind.Type)]
    public void Describe_ListsEachInjectedName_WithItsKind(string name, FishboneSymbolKind kind)
    {
        Assert.Equal(kind, Symbol(name).Kind);
    }

    [Fact]
    public void Describe_ListsEveryNameOnce()
    {
        var names = Config().Describe().Symbols.Select(symbol => symbol.Name).ToList();

        Assert.Equal(names.Distinct().Count(), names.Count);
        Assert.Equal(7, names.Count);
    }

    [Fact]
    public void Describe_ValuesAndConstants_HaveTheirValueType()
    {
        Assert.Equal(typeof(double), Symbol("PI").Type);
        Assert.Equal(typeof(Point), Symbol("origin").Type);
        Assert.Empty(Symbol("PI").Signatures);
    }

    [Fact]
    public void Describe_Delegate_HasOneSignatureWithHostNames()
    {
        var signature = Assert.Single(Symbol("add").Signatures);

        Assert.Equal(["first", "second"], signature.Parameters.Select(parameter => parameter.Name));
        Assert.All(signature.Parameters, parameter => Assert.Equal(typeof(int), parameter.Type));
        Assert.Equal(typeof(int), signature.ReturnType);
    }

    [Fact]
    public void Describe_MethodGroup_HasOneSignaturePerOverload()
    {
        var signatures = Symbol("abs").Signatures;

        Assert.Equal(typeof(Math).GetMethods().Count(m => m.Name == "Abs"), signatures.Count);
        Assert.Contains(signatures, signature => signature.Parameters.Single().Type == typeof(double));
    }

    [Fact]
    public void Describe_OutAndDefaultParameters_KeepDirectionAndDefault()
    {
        var parameters = Assert.Single(Symbol("tryParse").Signatures).Parameters;

        Assert.Equal(ParameterDirection.In, parameters[0].Direction);
        Assert.Equal(ParameterDirection.Out, parameters[1].Direction);
        // the type without the reference, int and not int&
        Assert.Equal(typeof(int), parameters[1].Type);
        Assert.False(parameters[1].HasDefault);
        Assert.True(parameters[2].HasDefault);
        Assert.Equal(3, parameters[2].DefaultValue);
    }

    [Fact]
    public void Describe_ManualCallable_UsesItsDeclaredParameters()
    {
        var signature = Assert.Single(Symbol("lookup").Signatures);

        Assert.Equal(["key", "found"], signature.Parameters.Select(parameter => parameter.Name));
        Assert.Equal(ParameterDirection.Out, signature.Parameters[1].Direction);
        Assert.Equal(typeof(object), signature.ReturnType);
    }

    [Fact]
    public void Describe_RegisteredType_ListsConstructorsByParameterCount()
    {
        var symbol = Symbol("Point");

        Assert.Equal(typeof(Point), symbol.Type);
        Assert.Equal([0, 2], symbol.Signatures.Select(signature => signature.Parameters.Count));
        Assert.All(symbol.Signatures, signature => Assert.Equal(typeof(Point), signature.ReturnType));
    }

    [Fact]
    public void Members_Instance_ListsPropertiesFieldsAndMethods()
    {
        var members = Config().Describe().Members(typeof(Point), isStatic: false);
        var names = members.Select(member => member.Name).ToList();

        Assert.Contains("X", names);
        Assert.Contains("Length", names);
        Assert.Contains("Label", names);
        Assert.Contains("Offset", names);
        Assert.Contains("ToString", names);
        // statics, accessors and private members aren't reachable from an instance
        Assert.DoesNotContain("Empty", names);
        Assert.DoesNotContain("get_X", names);
        Assert.DoesNotContain("Secret", names);
        Assert.Equal(names.Distinct().Count(), names.Count);
    }

    [Fact]
    public void Members_DescribeKindTypeAndWritability()
    {
        var members = Config().Describe().Members(typeof(Point), isStatic: false).ToDictionary(member => member.Name);

        Assert.Equal(FishboneMemberKind.Property, members["X"].Kind);
        Assert.Equal(typeof(int), members["X"].Type);
        Assert.True(members["X"].CanWrite);
        Assert.False(members["Length"].CanWrite);
        Assert.Equal(FishboneMemberKind.Field, members["Label"].Kind);
        Assert.True(members["Label"].CanWrite);
        Assert.False(members["Id"].CanWrite);
        Assert.Equal(FishboneMemberKind.Method, members["Offset"].Kind);
        Assert.False(members["Offset"].CanWrite);
        Assert.Empty(members["X"].Signatures);
    }

    [Fact]
    public void Members_OverloadedMethod_HasOneSignaturePerOverload()
    {
        var offset = Assert.Single(Config().Describe().Members(typeof(Point), isStatic: false), member => member.Name == "Offset");

        Assert.Equal(2, offset.Signatures.Count);
        Assert.Equal(typeof(Point), offset.Type);
    }

    [Fact]
    public void Members_OverriddenMethod_AppearsOnce()
    {
        var describe = Assert.Single(Config().Describe().Members(typeof(Derived), isStatic: false), member => member.Name == "Name");

        Assert.Single(describe.Signatures);
    }

    [Fact]
    public void Members_Static_ListsOnlyStaticMembers()
    {
        var members = Config().Describe().Members(typeof(Point), isStatic: true).ToDictionary(member => member.Name);

        Assert.Contains("Empty", members.Keys);
        Assert.Contains("Create", members.Keys);
        Assert.Contains("Origin", members.Keys);
        Assert.False(members["Origin"].CanWrite);
        Assert.DoesNotContain("X", members.Keys);
    }

    [Fact]
    public void Members_ScriptCollections_AreTheirDotNetTypes()
    {
        // a script dictionary is a Dictionary<object, object?> and a list is a List<object?>
        var description = Config().Describe();
        var dictionary = description.Members(typeof(Dictionary<object, object?>), isStatic: false).Select(member => member.Name).ToList();
        var list = description.Members(typeof(List<object?>), isStatic: false).Select(member => member.Name).ToList();

        Assert.Contains("ContainsKey", dictionary);
        Assert.Contains("Count", dictionary);
        Assert.Contains("Keys", dictionary);
        Assert.Contains("Add", list);
        Assert.Contains("Insert", list);
        // indexers are reached with [ ], not with a name
        Assert.DoesNotContain("Item", list);
    }

    public static bool TryParse(string text, out int value, int fallback = 3)
    {
        value = fallback;
        return int.TryParse(text, out value);
    }

    private sealed class LookupCallable : IManualCallable
    {
        public IReadOnlyList<CallableParameter> Parameters { get; } =
        [
            new CallableParameter("key", typeof(string), ParameterDirection.In),
            new CallableParameter("found", typeof(bool), ParameterDirection.Out)
        ];

        public object? Invoke(object?[] arguments) => null;
    }

    public sealed class Point
    {
        public const int Origin = 0;
        public static Point Empty { get; } = new();
        public static Point Create(int x, int y) => new(x, y);

        public Point() { }
        public Point(int x, int y) { X = x; Y = y; }

        public int X { get; set; }
        public int Y { get; set; }
        public double Length => Math.Sqrt(X * X + Y * Y);
        public string Label = "";
        public readonly int Id;
        private int Secret = 0;

        public Point Offset(int dx, int dy) => new(X + dx + Secret, Y + dy);
        public Point Offset(Point by) => new(X + by.X, Y + by.Y);
    }

    public class Base
    {
        public virtual string Name() => "base";
    }

    public sealed class Derived : Base
    {
        public override string Name() => "derived";
    }
}