namespace Fishbone.Interpreter.Tests;

public class CompoundAssignmentTests
{
    [Fact]
    public void Evaluate_CompoundAssignment_UpdatesVariablesForEveryOperator()
    {
        var env = InterpreterTestHelpers.Run("""
let plus = 10;
plus += 5;
let minus = 10;
minus -= 3;
let times = 10;
times *= 4;
let divide = 10;
divide /= 4;
let modulo = 10;
modulo %= 3;
""");

        Assert.Equal(15, env.GetValue("plus"));
        Assert.Equal(7, env.GetValue("minus"));
        Assert.Equal(40, env.GetValue("times"));
        Assert.Equal(2.5, env.GetValue("divide"));
        Assert.Equal(1, env.GetValue("modulo"));
    }

    [Fact]
    public void Evaluate_CompoundAssignment_UpdatesListElements()
    {
        var env = InterpreterTestHelpers.Run("""
let values = [10, 20, 30];
values[1] += 5;
values[2] *= 2;
""");

        var values = Assert.IsType<List<object>>(env.GetValue("values"));
        Assert.Equal([10, 25, 60], values);
    }

    [Fact]
    public void Evaluate_CompoundAssignment_UpdatesDictionaryValues()
    {
        var env = InterpreterTestHelpers.Run("""
let counts = {"a": 1, "b": 2};
counts["a"] += 10;
counts["b"] -= 1;
""");

        var counts = Assert.IsType<Dictionary<object, object?>>(env.GetValue("counts"));
        Assert.Equal(11, counts["a"]);
        Assert.Equal(1, counts["b"]);
    }

    [Fact]
    public void Evaluate_CompoundAssignment_OnStringConcatenates()
    {
        var env = InterpreterTestHelpers.Run("""
let greeting = "hello";
greeting += " world";
""");

        Assert.Equal("hello world", env.GetValue("greeting"));
    }

    [Fact]
    public void Evaluate_CompoundAssignment_OnUndeclaredVariable_Throws()
    {
        Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("undeclared += 1;"));
    }
}
