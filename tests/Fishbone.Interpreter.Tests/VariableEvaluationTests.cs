using Fishbone;

namespace Fishbone.Interpreter.Tests;

public class VariableEvaluationTests
{
    [Fact]
    public void Evaluate_DeclarationsAndAssignments_UpdateEnvironment()
    {
        var env = InterpreterTestHelpers.Run("""
let x = 5;
x = x + 7;
""");

        Assert.Equal(12, env.GetValue("x"));
    }

    [Fact]
    public void Evaluate_BlockScope_HidesLocalDeclarationsButAllowsOuterAssignment()
    {
        var env = InterpreterTestHelpers.Run("""
let outer = 1;
{
    let inner = 2;
    outer = inner + 1;
}
""");

        Assert.Equal(3, env.GetValue("outer"));
        Assert.Throws<FishboneRuntimeException>(() => env.GetValue("inner"));
    }

    [Fact]
    public void Evaluate_InvalidVariableOperations_Throw()
    {
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("missing = 1;"));
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("let duplicate = 1; let duplicate = 2;"));
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("let value = missing;"));
    }
}