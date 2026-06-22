using Fishbone.Core;
using Fishbone.Debugging;
using Fishbone.Parser;

namespace Fishbone.Interpreter.Tests;

public class RuntimeExceptionLocationTests
{
    [Fact]
    public void UndefinedVariable_ReportsLocationOfIdentifier()
    {
        var ast = ASTParser.Parse("let x = missing;");
        var interpreter = new FishboneInterpreter();

        var ex = Assert.Throws<FishboneRuntimeException>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));
        Assert.Equal(1, ex.Line);
        Assert.Equal(9, ex.Column);
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void UndefinedVariableInExpression_ReportsInnermostNodeLocation()
    {
        var ast = ASTParser.Parse("let result = missing + 1;");
        var interpreter = new FishboneInterpreter();

        var ex = Assert.Throws<FishboneRuntimeException>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));
        Assert.Equal(1, ex.Line);
        Assert.Equal(14, ex.Column);
    }

    [Fact]
    public void UndefinedVariableInsideFunction_ReportsLocationInsideFunctionBody()
    {
        var ast = ASTParser.Parse("""
func fail() {
    return missing;
}
fail();
""");
        var interpreter = new FishboneInterpreter();

        var ex = Assert.Throws<FishboneRuntimeException>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));
        Assert.Equal(2, ex.Line);
        Assert.Equal(12, ex.Column);
    }

    [Fact]
    public void FishboneRuntimeException_DerivesFromException()
    {
        var ast = ASTParser.Parse("let value = missing;");
        var interpreter = new FishboneInterpreter();

        Assert.ThrowsAny<Exception>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));
    }

    [Fact]
    public void RuntimeException_PreservesOriginalMessage()
    {
        var ast = ASTParser.Parse("let value = missing;");
        var interpreter = new FishboneInterpreter();

        var ex = Assert.Throws<FishboneRuntimeException>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));
        Assert.Equal($"Undefined variable \"missing\".", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void RuntimeException_IsReportedOnceAtInnermostNode()
    {
        var debugger = new RecordingDebugger();
        var interpreter = new FishboneInterpreter(debugger: debugger);
        var ast = ASTParser.Parse("let result = missing + 1;");

        Assert.Throws<FishboneRuntimeException>(() => interpreter.Evaluate(new FishboneEnvironment(), ast));

        var report = Assert.Single(debugger.Exceptions);
        Assert.IsType<IdentifierNode>(report.Node);
    }

    private sealed class RecordingDebugger : IFishboneDebugger
    {
        public List<(Exception Exception, AstNode Node)> Exceptions { get; } = [];

        public void OnExecutionStarted(AstNode root, FishboneEnvironment environment) { }
        public void OnBeforeExecute(AstNode node, FishboneEnvironment environment) { }
        public void OnRuntimeException(Exception exception, AstNode node, FishboneEnvironment environment)
            => Exceptions.Add((exception, node));
        public void OnFunctionEnter(string functionName, FishboneEnvironment environment) { }
        public void OnFunctionExit(string functionName) { }
        public void OnExecutionCompleted(FishboneEnvironment environment) { }
    }
}