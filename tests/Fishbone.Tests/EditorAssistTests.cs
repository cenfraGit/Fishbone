namespace Fishbone.Tests;

/// <summary>
/// <see cref="FishboneAnalysis.CompletionsAt"/> and <see cref="FishboneAnalysis.CallAt"/> answer an
/// editor while the user types. The text usually doesn't parse, so they read tokens. A <c>|</c> in
/// each script marks the caret.
/// </summary>
public class EditorAssistTests
{
    private sealed class Point
    {
        public int X { get; set; }
        public Point Moved(int dx) => this;
        public Point Moved(int dx, int dy) => this;
    }

    private delegate void ReadDelegate(out Point point, string name);

    private static readonly FishboneDescription Description = new FishboneConfiguration()
        .AddType<Point>()
        .AddValue("origin", new Point())
        .AddBuiltIn("read", new ReadDelegate((out Point point, string name) => point = new Point()))
        .Describe();

    // the analysis is made from the script without its caret line, like an editor's last parse
    private static (FishboneAnalysis Analysis, string Text, int Caret) At(string script)
    {
        int caret = script.IndexOf('|');
        string text = script.Remove(caret, 1);
        int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        return (FishboneAnalysis.Analyze(text[..lineStart], Description), text, caret);
    }

    private static List<string>? Completions(string script)
    {
        var (analysis, text, caret) = At(script);
        return analysis.CompletionsAt(text, caret)?.Items.Select(item => item.Text).ToList();
    }

    private static FishboneCallInfo? Call(string script)
    {
        var (analysis, text, caret) = At(script);
        return analysis.CallAt(text, caret);
    }

    [Fact]
    public void Completions_FilterByTheWordBeingTyped()
    {
        var (analysis, text, caret) = At("let originalName = 1;\norig|");
        var completions = analysis.CompletionsAt(text, caret)!;

        Assert.Equal(text.Length - 4, completions.Start);
        Assert.Equal(["originalName", "origin"], completions.Items.Select(item => item.Text));
        Assert.Equal(FishboneSuggestionKind.Variable, completions.Items[0].Kind);
        Assert.Equal(FishboneSuggestionKind.Value, completions.Items[1].Kind);
    }

    [Fact]
    public void Completions_IncludeKeywords()
    {
        Assert.Contains("foreach", Completions("for|")!);
    }

    [Fact]
    public void Completions_AfterADot_AreTheMembers()
    {
        var members = Completions("let p = Point();\np.|")!;
        Assert.Contains("X", members);
        Assert.Contains("Moved", members);
        Assert.DoesNotContain("let", members);

        Assert.Equal(["X"], Completions("let p = Point();\np.X|"));
        Assert.Contains("Length", Completions("\"text\".|")!);
        Assert.Contains("X", Completions("Point().Moved(1).|")!);
    }

    [Theory]
    [InlineData("1.|")]
    [InlineData("nothing.|")]
    [InlineData("// origin.|")]
    [InlineData("/* origin.|")]
    [InlineData("println(\"origin.|")]
    public void Completions_InCommentsStringsAndNumbers_AreNull(string script)
    {
        Assert.Null(Completions(script));
    }

    [Fact]
    public void Call_GivesTheOverloadsAndTheArgument()
    {
        var call = Call("read(out p, |")!;

        Assert.Equal("read", call.Name);
        Assert.Equal(4, call.OpenParen);
        Assert.Equal(1, call.Argument);
        Assert.Equal(ParameterDirection.Out, Assert.Single(call.Signatures).Parameters[0].Direction);
    }

    [Fact]
    public void Call_IgnoresCommasInNestedCallsAndStrings()
    {
        Assert.Equal(2, Call("read(out p, Point(1, 2), \"a,b\"|")!.Argument);
    }

    [Fact]
    public void Call_OnAMethod_GivesItsOverloads()
    {
        var call = Call("let p = Point();\np.Moved(1, |")!;

        Assert.Equal("Moved", call.Name);
        Assert.Equal(2, call.Signatures.Count);
        Assert.Equal(1, call.Argument);
    }

    [Fact]
    public void Call_ToAScriptFunction_GivesItsParameterNames()
    {
        var call = Call("func area(width, height) { return 0; }\narea(|")!;

        Assert.Equal(["width", "height"], Assert.Single(call.Signatures).Parameters.Select(parameter => parameter.Name));
    }

    [Theory]
    [InlineData("read(1);|")]
    [InlineData("read(1); x|")]
    [InlineData("read([1, |")]
    [InlineData("nothing(|")]
    [InlineData("read(\"abc|")]
    public void Call_OutsideAKnownCall_IsNull(string script)
    {
        Assert.Null(Call(script));
    }
}
