using Fishbone.Core;
using Fishbone.Debugging;
using Fishbone.Parser;

namespace Fishbone.Interpreter.Tests;

public class DebuggerHookTests
{
    [Fact]
    public void EvaluateNotifiesDebuggerBeforeEveryVisitedNode()
    {
        var ast = ASTParser.Parse("let result = 1 + 2;");
        var debugger = new RecordingDebugger();
        var interpreter = new FishboneInterpreter(debugger: debugger);

        interpreter.Evaluate(new FishboneEnvironment(), ast);

        Assert.Equal(
            [nameof(ProgramNode), nameof(DeclarationNode), nameof(BinaryOpNode), nameof(LiteralNode), nameof(LiteralNode)],
            debugger.BeforeNodes.Select(node => node.GetType().Name));
    }

    [Fact]
    public void FunctionExitRunsAfterReturnAndRuntimeFailure()
    {
        var returned = RunWithDebugger("func work() { return 4; } let value = work();");
        Assert.Contains("enter:work", returned.Events);
        Assert.Contains("exit:work", returned.Events);
        Assert.True(returned.Events.IndexOf("enter:work") < returned.Events.IndexOf("exit:work"));

        var failed = new RecordingDebugger();
        var interpreter = new FishboneInterpreter(debugger: failed);
        var ast = ASTParser.Parse("func fail() { return missing; } fail();");
        Assert.ThrowsAny<Exception>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));
        Assert.Contains("exit:fail", failed.Events);
    }

    [Fact]
    public void RuntimeExceptionIsReportedOnlyAtInnermostFailingNode()
    {
        var debugger = new RecordingDebugger();
        var interpreter = new FishboneInterpreter(debugger: debugger);
        var ast = ASTParser.Parse("let result = missing + 1;");

        Assert.ThrowsAny<Exception>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));

        var report = Assert.Single(debugger.Exceptions);
        Assert.IsType<IdentifierNode>(report.Node);
    }

    private static RecordingDebugger RunWithDebugger(string code)
    {
        var debugger = new RecordingDebugger();
        new FishboneInterpreter(debugger: debugger).Evaluate(new FishboneEnvironment(), ASTParser.Parse(code));
        return debugger;
    }

    private sealed class RecordingDebugger : IFishboneDebugger
    {
        public List<AstNode> BeforeNodes { get; } = [];
        public List<(Exception Exception, AstNode Node)> Exceptions { get; } = [];
        public List<string> Events { get; } = [];

        public void OnExecutionStarted(AstNode root, FishboneEnvironment environment) => Events.Add("started");
        public void OnBeforeExecute(AstNode node, FishboneEnvironment environment) => BeforeNodes.Add(node);
        public void OnRuntimeException(Exception exception, AstNode node, FishboneEnvironment environment) => Exceptions.Add((exception, node));
        public void OnFunctionEnter(string functionName, FishboneEnvironment environment) => Events.Add($"enter:{functionName}");
        public void OnFunctionExit(string functionName) => Events.Add($"exit:{functionName}");
        public void OnExecutionCompleted(FishboneEnvironment environment) => Events.Add("completed");
    }
}