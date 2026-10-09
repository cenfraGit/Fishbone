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
