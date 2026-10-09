namespace Fishbone.Tests;

/// <summary>
/// <see cref="FishboneExpression.Evaluate"/> and <see cref="FishboneAnalysis.WatchCompletionsAt"/>,
/// what a debugger's watch uses on a script's values.
/// </summary>
public class WatchExpressionTests
{
    private const string Source = "func same(value) { return value; }\nlet first = 2;\nlet words = same(\"text\");\nlet second = [1, 2];\n";

    private static readonly FishboneEnvironment Values = FishboneProgram.Run(Source);

    private static readonly FishboneAnalysis Analysis = FishboneAnalysis.Analyze(Source, new FishboneConfiguration().Describe());

    [Fact]
    public void Evaluate_ReadsTheScriptsValues()
    {
        Assert.Equal(6, FishboneExpression.Evaluate("first * 3", Values));
        Assert.Equal(2, FishboneExpression.Evaluate("second.Count", Values));
        Assert.Equal("text", FishboneExpression.Evaluate("same(words)", Values));
    }

    [Fact]
    public void Evaluate_ThrowsForAnInvalidOrFailingExpression()
    {
        Assert.Throws<FishboneParseException>(() => FishboneExpression.Evaluate("first +", Values));
        Assert.Throws<FishboneRuntimeException>(() => FishboneExpression.Evaluate("nope", Values));
    }

    [Fact]
    public void Evaluate_StopsWhenCancelled()
    {
        var environment = FishboneProgram.Run("func forever() { while (true) { } }");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => FishboneExpression.Evaluate("forever()", environment, null, cancelled.Token));
    }

    // like a HALCON operator: nothing comes back, the results are out arguments
    private delegate void CountDelegate(List<object?> items, out int count);
    private delegate void SplitDelegate(int value, out int tens, out int ones);

    private static readonly FishboneConfiguration Operators = new FishboneConfiguration()
        .AddBuiltIn("count_items", new CountDelegate((List<object?> items, out int count) => count = items.Count))
        .AddBuiltIn("split", new SplitDelegate((int value, out int tens, out int ones) => (tens, ones) = (value / 10, value % 10)));

    [Fact]
    public void Evaluate_ACallThatReturnsNothing_GivesItsOutValues()
    {
        var environment = FishboneProgram.Run("let items = [1, 2, 3]; let ones = 7;", Operators);

        Assert.Equal(3, FishboneExpression.Evaluate("count_items(items, out n)", environment, Operators));
        var both = Assert.IsAssignableFrom<IDictionary<string, object?>>(FishboneExpression.Evaluate("split(42, out tens, out ones)", environment, Operators));

        Assert.Equal(4, both["tens"]);
        Assert.Equal(2, both["ones"]);
        // the outs stay in the watch: the script's ones is untouched, and n never appears
        Assert.Equal(7, environment.GetValue("ones"));
        Assert.False(environment.IsDefined("n"));
    }

    // like count_obj, which only takes HALCON objects
    public sealed class Region;
    public sealed class Picture;
    private delegate void CountRegionsDelegate(Region regions, out int count);

    [Fact]
    public void Evaluate_AValueOfTheWrongType_SaysWhatItTakes()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("count_regions", new CountRegionsDelegate((Region regions, out int count) => count = 1))
            .AddValue("photo", new Picture());
        var environment = FishboneProgram.Run("", config);

        var error = Assert.Throws<FishboneRuntimeException>(() => FishboneExpression.Evaluate("count_regions(photo, out n)", environment, config));

        Assert.Equal("'count_regions' argument 1 ('regions') takes 'Region', not 'Picture'.", error.Message);
    }

    [Fact]
    public void WatchCompletions_OfferTheNamesInScopeAtTheLine()
    {
        // at line 3, second isn't declared yet. after the script, it is
        var atLine3 = Analysis.WatchCompletionsAt(Source, 3, "1 + s", 5);
        var atEnd = Analysis.WatchCompletionsAt(Source, 99, "1 + s", 5);

        Assert.NotNull(atLine3);
        Assert.Equal(4, atLine3.Start);
        Assert.Contains(atLine3.Items, item => item.Text == "same");
        Assert.DoesNotContain(atLine3.Items, item => item.Text == "second");
        Assert.Contains(atEnd!.Items, item => item.Text == "second");
    }

    [Fact]
    public void WatchCompletions_ListTheMembersOfAValue_WhenTheScriptDoesntSayItsType()
    {
        // a script function's result has no known type, but its value does
        Func<string, object?> evaluate = expression => FishboneExpression.Evaluate(expression, Values);

        Assert.Null(Analysis.WatchCompletionsAt(Source, 99, "words.Len", 9));
        var members = Analysis.WatchCompletionsAt(Source, 99, "words.Len", 9, evaluate);

        Assert.NotNull(members);
        Assert.Equal(6, members.Start);
        Assert.Equal("Length", Assert.Single(members.Items).Text);
    }

    [Fact]
    public void WatchCompletions_DontEvaluateACall()
    {
        int evaluated = 0;
        Func<string, object?> evaluate = expression => { evaluated++; return FishboneExpression.Evaluate(expression, Values); };

        Assert.Null(Analysis.WatchCompletionsAt(Source, 99, "same(words).Len", 15, evaluate));
        Assert.Equal(0, evaluated);
    }
}
