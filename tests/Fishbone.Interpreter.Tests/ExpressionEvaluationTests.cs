namespace Fishbone.Interpreter.Tests;

public class ExpressionEvaluationTests
{
    [Fact]
    public void Evaluate_ArithmeticAndComparisonExpressions_ProducesExpectedValues()
    {
        var env = InterpreterTestHelpers.Run("""
let arithmetic = 10 + 3 * 4 - 6 / 2;
let comparison = arithmetic >= 19 == true;
let lessThan = arithmetic < 20;
let notEqual = arithmetic != 0;
""");

        Assert.Equal(19, env.GetValue("arithmetic"));
        Assert.Equal(true, env.GetValue("comparison"));
        Assert.Equal(true, env.GetValue("lessThan"));
        Assert.Equal(true, env.GetValue("notEqual"));
    }

    [Fact]
    public void Evaluate_UnaryExpressions_ProducesExpectedValues()
    {
        var env = InterpreterTestHelpers.Run("""
let negative = -5;
let inverted = not true;
let zeroIsFalsey = not 0;
let nonZeroIsTruthy = not 3;
""");

        Assert.Equal(-5, env.GetValue("negative"));
        Assert.Equal(false, env.GetValue("inverted"));
        Assert.Equal(true, env.GetValue("zeroIsFalsey"));
        Assert.Equal(false, env.GetValue("nonZeroIsTruthy"));
    }

    [Fact]
    public void Evaluate_Truthiness_HandlesNullBooleansNumbersAndStrings()
    {
        var env = InterpreterTestHelpers.Run("""
func oneValue()
{
    return 1;
}

let value, missing = oneValue();
let nullIsFalsey = not missing;
let falseIsFalsey = not false;
let trueIsTruthy = not true;
let zeroDoubleIsFalsey = not 0.0;
let nonZeroDoubleIsTruthy = not 0.5;
let emptyStringIsFalsey = not "";
let nonEmptyStringIsTruthy = not "hello";
""");

        Assert.Equal(true, env.GetValue("nullIsFalsey"));
        Assert.Equal(true, env.GetValue("falseIsFalsey"));
        Assert.Equal(false, env.GetValue("trueIsTruthy"));
        Assert.Equal(true, env.GetValue("zeroDoubleIsFalsey"));
        Assert.Equal(false, env.GetValue("nonZeroDoubleIsTruthy"));
        Assert.Equal(true, env.GetValue("emptyStringIsFalsey"));
        Assert.Equal(false, env.GetValue("nonEmptyStringIsTruthy"));
    }

    [Fact]
    public void Evaluate_StringConcatenation_UsesStringLiteralValues()
    {
        var env = InterpreterTestHelpers.Run("""let greeting = "hello" + " " + "world";""");

        Assert.Equal("hello world", env.GetValue("greeting"));
    }
}
