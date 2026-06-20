using Fishbone.Core;

namespace Fishbone.Interpreter.Tests;

public class CancellationTests
{
    [Fact]
    public void Evaluate_WithDefaultToken_EvaluatesNormally()
    {
        var interpreter = new FishboneInterpreter();

        var result = interpreter.Evaluate(new FishboneEnvironment(), new LiteralNode(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_WithCancelledToken_ThrowsOperationCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var interpreter = new FishboneInterpreter(cancellation.Token);

        Assert.Throws<OperationCanceledException>(() =>
            interpreter.Evaluate(new FishboneEnvironment(), new LiteralNode(42)));
    }
}