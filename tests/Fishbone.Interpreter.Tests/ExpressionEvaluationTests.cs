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

        Assert.Equal(19.0, env.GetValue("arithmetic"));
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
    public void Evaluate_BooleanOperators_ProduceExpectedTruthTable()
    {
        var env = InterpreterTestHelpers.Run("""
let andTrue = true and true;
let andFalse = true and false;
let orTrue = false or true;
let orFalse = false or false;
let xorTrue = true xor false;
let xorFalse = true xor true;
""");

        Assert.Equal(true, env.GetValue("andTrue"));
        Assert.Equal(false, env.GetValue("andFalse"));
        Assert.Equal(true, env.GetValue("orTrue"));
        Assert.Equal(false, env.GetValue("orFalse"));
        Assert.Equal(true, env.GetValue("xorTrue"));
        Assert.Equal(false, env.GetValue("xorFalse"));
    }

    [Fact]
    public void Evaluate_BooleanOperators_UseTruthinessAndReturnBooleans()
    {
        var env = InterpreterTestHelpers.Run("""
let numberAndString = 1 and "value";
let zeroOrEmpty = 0 or "";
let nullOrObject = null or [1];
let xorValues = "value" xor 0;
""");

        Assert.IsType<bool>(env.GetValue("numberAndString"));
        Assert.IsType<bool>(env.GetValue("zeroOrEmpty"));
        Assert.IsType<bool>(env.GetValue("nullOrObject"));
        Assert.IsType<bool>(env.GetValue("xorValues"));
        Assert.Equal(true, env.GetValue("numberAndString"));
        Assert.Equal(false, env.GetValue("zeroOrEmpty"));
        Assert.Equal(true, env.GetValue("nullOrObject"));
        Assert.Equal(true, env.GetValue("xorValues"));
    }

    [Fact]
    public void Evaluate_AndOr_ShortCircuitWhileXorEvaluatesBothSides()
    {
        var env = InterpreterTestHelpers.Run("""
let skippedAnd = false and missingAnd;
let skippedOr = true or missingOr;
""");

        Assert.Equal(false, env.GetValue("skippedAnd"));
        Assert.Equal(true, env.GetValue("skippedOr"));
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("let result = true xor missingXor;"));
    }

    [Fact]
    public void Evaluate_StringConcatenation_UsesStringLiteralValues()
    {
        var env = InterpreterTestHelpers.Run("""let greeting = "hello" + " " + "world";""");

        Assert.Equal("hello world", env.GetValue("greeting"));
    }

    [Fact]
    public void Evaluate_ListExpressions_ProducesEvaluatedRuntimeLists()
    {
        var env = InterpreterTestHelpers.Run("""
let x = 10;
let empty = [];
let values = [1, x, x + 5, "hi"];
let nested = [[1, 2], [3]];
""");

        var empty = Assert.IsType<List<object>>(env.GetValue("empty"));
        Assert.Empty(empty);

        var values = Assert.IsType<List<object>>(env.GetValue("values"));
        Assert.Equal([1, 10, 15, "hi"], values);

        var nested = Assert.IsType<List<object>>(env.GetValue("nested"));
        Assert.Equal([1, 2], Assert.IsType<List<object>>(nested[0]));
        Assert.Equal([3], Assert.IsType<List<object>>(nested[1]));
    }

    [Fact]
    public void Evaluate_ListExpressions_CanBeDestructured()
    {
        var env = InterpreterTestHelpers.Run("""
let first, second = [1, 2];
""");

        Assert.Equal(1, env.GetValue("first"));
        Assert.Equal(2, env.GetValue("second"));
    }

    [Fact]
    public void Evaluate_DictionaryExpressions_ProducesEvaluatedRuntimeDictionaries()
    {
        var env = InterpreterTestHelpers.Run("""
let key = "answer";
let x = 10;
let empty = {};
let values = {key: x + 5, 2: "two", true: false};
let nested = {"list": [1, x], "dict": {"inner": x + 1}};
""");

        var empty = Assert.IsType<Dictionary<object, object?>>(env.GetValue("empty"));
        Assert.Empty(empty);

        var values = Assert.IsType<Dictionary<object, object?>>(env.GetValue("values"));
        Assert.Equal(15, values["answer"]);
        Assert.Equal("two", values[2]);
        Assert.Equal(false, values[true]);

        var nested = Assert.IsType<Dictionary<object, object?>>(env.GetValue("nested"));
        Assert.Equal([1, 10], Assert.IsType<List<object>>(nested["list"]));

        var inner = Assert.IsType<Dictionary<object, object?>>(nested["dict"]);
        Assert.Equal(11, inner["inner"]);
    }

    [Fact]
    public void Evaluate_IndexingExpressions_ReadsListsAndDictionaries()
    {
        var env = InterpreterTestHelpers.Run("""
let values = [10, 20, 30];
let first = values[0];
let i = 1;
let expressionIndex = values[i + 1];
let keyed = {"name": "Fishbone", 7: "seven"};
let name = keyed["name"];
let number = keyed[7];
let matrix = [[1, 2], [3, 4]];
let nestedListValue = matrix[1][0];
let nestedDictValue = {"outer": {"inner": 42}}["outer"]["inner"];
""");

        Assert.Equal(10, env.GetValue("first"));
        Assert.Equal(30, env.GetValue("expressionIndex"));
        Assert.Equal("Fishbone", env.GetValue("name"));
        Assert.Equal("seven", env.GetValue("number"));
        Assert.Equal(3, env.GetValue("nestedListValue"));
        Assert.Equal(42, env.GetValue("nestedDictValue"));
    }

    [Fact]
    public void Evaluate_IntegerDivision_ReturnsDouble()
    {
        var env = InterpreterTestHelpers.Run("""
let result = 5 / 2;
""");

        Assert.Equal(2.5, env.GetValue("result"));
    }

    [Fact]
    public void Evaluate_IntegerDivisionByZero_ReturnsInfinity()
    {
        var env = InterpreterTestHelpers.Run("""
let result = 5 / 0;
""");

        Assert.Equal(double.PositiveInfinity, env.GetValue("result"));
    }

    [Fact]
    public void Evaluate_MixedTypeDivision_KeepsDoubleResult()
    {
        var env = InterpreterTestHelpers.Run("""
let a = 5.0 / 2;
let b = 5 / 2.0;
""");

        Assert.Equal(2.5, env.GetValue("a"));
        Assert.Equal(2.5, env.GetValue("b"));
    }

    [Fact]
    public void Evaluate_NegativeIntegerDivision_ReturnsDouble()
    {
        var env = InterpreterTestHelpers.Run("""
let result = -5 / 2;
""");

        Assert.Equal(-2.5, env.GetValue("result"));
    }
}