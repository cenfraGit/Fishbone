namespace Fishbone.Interpreter.Tests;

public class FunctionEvaluationTests
{
    [Fact]
    public void Evaluate_FunctionParametersAndReturn_AssignResultToCaller()
    {
        var env = InterpreterTestHelpers.Run("""
func calculatePower(base, multiplier)
{
    let result = base * multiplier;
    result = result + 5;
    return result;
}

let finalAnswer = calculatePower(10, 3);
""");

        Assert.Equal(35, env.GetValue("finalAnswer"));
        Assert.Throws<Exception>(() => env.GetValue("result"));
    }

    [Fact]
    public void Evaluate_FunctionWithoutReturn_ReturnsNull()
    {
        var env = InterpreterTestHelpers.Run("""
func doNothing()
{
    let local = 1;
}

let result = doNothing();
""");

        Assert.Null(env.GetValue("result"));
    }

    [Fact]
    public void Evaluate_FunctionClosure_CanMutateOuterScope()
    {
        var env = InterpreterTestHelpers.Run("""
let counter = 0;

func increment()
{
    counter = counter + 1;
    return counter;
}

let firstCall = increment();
let secondCall = increment();
""");

        Assert.Equal(1, env.GetValue("firstCall"));
        Assert.Equal(2, env.GetValue("secondCall"));
        Assert.Equal(2, env.GetValue("counter"));
    }

    [Fact]
    public void Evaluate_Return_EscapesNestedLoopAndIfBlock()
    {
        var env = InterpreterTestHelpers.Run("""
func searchTarget(target)
{
    let i = 0;
    while (i < 10)
    {
        if (i == target)
        {
            return i * 100;
        }
        i = i + 1;
    }
    return -1;
}

let successResult = searchTarget(4);
let failResult = searchTarget(15);
""");

        Assert.Equal(400, env.GetValue("successResult"));
        Assert.Equal(-1, env.GetValue("failResult"));
    }

    [Fact]
    public void Evaluate_FunctionCallWithWrongArity_Throws()
    {
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("""
func add(left, right)
{
    return left + right;
}

let value = add(1);
"""));
    }

    [Fact]
    public void Evaluate_NonCallableSymbolCall_Throws()
    {
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("""
let value = 1;
let result = value();
"""));
    }
}