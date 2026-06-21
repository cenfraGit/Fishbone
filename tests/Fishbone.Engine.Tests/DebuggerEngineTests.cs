using Fishbone.Core;
using Fishbone.Debugging;

namespace Fishbone.Engine.Tests;

public class DebuggerEngineTests
{
    [Fact]
    public void RunWithoutDebuggerRetainsExistingBehavior()
    {
        var environment = FishboneEngine.Run("let answer = 42;", new FishboneConfiguration());
        Assert.Equal(42, environment.GetValue("answer"));
    }

    [Fact]
    public void RunPublishesExecutionLifecycle()
    {
        var debugger = new LifecycleDebugger();
        FishboneEngine.Run("let answer = 42;", new FishboneConfiguration(), debugger: debugger);
        Assert.Equal(["started", "completed"], debugger.Events);
    }

    private sealed class LifecycleDebugger : IFishboneDebugger
    {
        public List<string> Events { get; } = [];
        public void OnExecutionStarted(AstNode root, FishboneEnvironment environment) => Events.Add("started");
        public void OnBeforeExecute(AstNode node, FishboneEnvironment environment) { }
        public void OnRuntimeException(Exception exception, AstNode node, FishboneEnvironment environment) { }
        public void OnFunctionEnter(string functionName, FishboneEnvironment environment) { }
        public void OnFunctionExit(string functionName) { }
        public void OnExecutionCompleted(FishboneEnvironment environment) => Events.Add("completed");
    }
}