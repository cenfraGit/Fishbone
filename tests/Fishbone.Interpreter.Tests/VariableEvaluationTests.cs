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

    [Theory]
    [InlineData("missing = 1;", "Undefined variable 'missing'.")]
    [InlineData("let duplicate = 1; let duplicate = 2;", "Variable 'duplicate' is already declared.")]
    [InlineData("let value = missing;", "Undefined variable 'missing'.")]
    public void Evaluate_InvalidVariableOperations_Throw(string code, string message)
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => InterpreterTestHelpers.Run(code));

        Assert.Equal(message, exception.Message);
    }
}