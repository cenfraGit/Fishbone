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
    public void Evaluate_MultipleValueDestructuring_AssignsReturnedValues()
    {
        var env = InterpreterTestHelpers.Run("""
func getCoordinates()
{
    return 20, 100;
}

let x, y = getCoordinates();
""");

        Assert.Equal(20, env.GetValue("x"));
        Assert.Equal(100, env.GetValue("y"));
    }

    [Fact]
    public void Evaluate_DestructuringWithTooFewValues_AssignsNullToMissingNames()
    {
        var env = InterpreterTestHelpers.Run("""
func oneValue()
{
    return 10;
}

let first, second = oneValue();
""");

        Assert.Equal(10, env.GetValue("first"));
        Assert.Null(env.GetValue("second"));
    }

    [Fact]
    public void Evaluate_DestructuringWithExtraValues_IgnoresExtraValues()
    {
        var env = InterpreterTestHelpers.Run("""
func threeValues()
{
    return 1, 2, 3;
}

let first, second = threeValues();
""");

        Assert.Equal(1, env.GetValue("first"));
        Assert.Equal(2, env.GetValue("second"));
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
        Assert.Throws<Exception>(() => env.GetValue("inner"));
    }

    [Fact]
    public void Evaluate_InvalidVariableOperations_Throw()
    {
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("missing = 1;"));
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("let duplicate = 1; let duplicate = 2;"));
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("let value = missing;"));
    }
}