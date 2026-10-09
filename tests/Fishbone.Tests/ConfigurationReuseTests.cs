using Fishbone.Ast;
using Fishbone.Debugging;

namespace Fishbone.Tests;

/// <summary>
/// A host builds a configuration once and runs many scripts with it, or clones it per run. Runs
/// must not leak into each other or into the configuration.
/// </summary>
public class ConfigurationReuseTests
{
    [Fact]
    public void Clone_CopiesEverything_AndTheCopyIsIndependent()
    {
        // Clone copies a fixed list of members. a new public property has to be added to Clone
        // and here, or a clone would silently drop it
        Assert.Equal(
            ["BuiltIns", "EnableMemberAccess", "TypeConverters", "Values", "Visualizers"],
            typeof(FishboneConfiguration).GetProperties().Select(property => property.Name).Order());

        var original = new FishboneConfiguration { EnableMemberAccess = false }
            .AddBuiltIn("f", new Func<int>(() => 1))
            .AddValue("v", 2)
            .AddTypeConverter(typeof(Guid), value => value)
            .AddVisualizer<Guid>(_ => null);

        var clone = original.Clone();
        clone.AddBuiltIn("g", 1).AddValue("w", 1).AddTypeConverter(typeof(Uri), value => value).AddVisualizer<Uri>(_ => null);

        Assert.False(clone.EnableMemberAccess);
        Assert.Equal(["f", "g"], clone.BuiltIns.Keys.Order());
        Assert.Equal(["v", "w"], clone.Values.Keys.Order());
        Assert.Equal(2, clone.TypeConverters.Count);
        Assert.Equal(2, clone.Visualizers.Count);
        Assert.Equal(["f"], original.BuiltIns.Keys);
        Assert.Equal(["v"], original.Values.Keys);
        Assert.Single(original.TypeConverters);
        Assert.Single(original.Visualizers);
    }

    [Fact]
    public void Reuse_EachRunStartsFromTheConfiguredValues()
    {
        var config = new FishboneConfiguration().AddValue("n", 0);

        var first = FishboneProgram.Run("n = n + 1; let made = 1;", config);
        var second = FishboneProgram.Run("n = n + 1;", config);

        Assert.Equal(1, first.GetValue("n"));
        Assert.Equal(1, second.GetValue("n"));
        Assert.False(second.IsDefined("made"));
        Assert.Equal(0, config.Values["n"]);
    }

    [Fact]
    public void Reuse_AMutableValueIsSharedByReference()
    {
        // a value is the host's object, not a copy, so changes to it outlive the run
        var items = new List<object?>();
        var config = new FishboneConfiguration().AddValue("items", items);

        FishboneProgram.Run("items.Add(1);", config);
        FishboneProgram.Run("items.Add(2);", config);

        Assert.Equal([1, 2], items);
    }

    [Theory]
    [InlineData("let x = 1;", false)]
    [InlineData("let x = missing;", false)]
    [InlineData("while (true) { }", true)]
    public void Debugger_IsToldTheRunCompleted_HoweverItEnds(string code, bool cancel)
    {
        // a debug host waits for this to release the session, so it has to come on errors too
        var debugger = new CompletionRecorder();
        using var cancellation = new CancellationTokenSource();
        if (cancel)
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        try
        {
            FishboneProgram.Run(code, new FishboneConfiguration(), debugger, cancellation.Token);
        }
        catch (FishboneRuntimeException) { }
        catch (OperationCanceledException) { }

        Assert.Equal(1, debugger.Completed);
    }

    private sealed class CompletionRecorder : IFishboneDebugger
    {
        public int Completed;

        public void OnExecutionStarted(AstNode root, FishboneEnvironment environment) { }
        public void OnBeforeExecute(AstNode node, FishboneEnvironment environment) { }
        public void OnRuntimeException(Exception exception, AstNode node, FishboneEnvironment environment) { }
        public void OnFunctionEnter(string functionName, FishboneEnvironment environment) { }
        public void OnFunctionExit(string functionName) { }
        public void OnExecutionCompleted(FishboneEnvironment environment) => Completed++;
    }
}
