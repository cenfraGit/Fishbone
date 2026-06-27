namespace Fishbone.Interpreter.Tests;

public class ControlFlowEvaluationTests
{
    [Fact]
    public void Evaluate_IfElse_ExecutesCorrectPath()
    {
        var env = InterpreterTestHelpers.Run("""
let score = 85;
let grade = 0;

if (score > 80)
{
    grade = 1;
}
else
{
    grade = 3;
}
""");

        Assert.Equal(1, env.GetValue("grade"));
    }

    [Fact]
    public void Evaluate_ElseIfChain_ExecutesFirstTruthyBranchOnly()
    {
        var env = InterpreterTestHelpers.Run("""
let score = 75;
let grade = 0;
let executedBranches = 0;

if (score > 90)
{
    grade = 1;
    executedBranches = executedBranches + 1;
}
else if (score > 80)
{
    grade = 2;
    executedBranches = executedBranches + 1;
}
else if (score > 70)
{
    grade = 3;
    executedBranches = executedBranches + 1;
}
else
{
    grade = 4;
    executedBranches = executedBranches + 1;
}
""");

        Assert.Equal(3, env.GetValue("grade"));
        Assert.Equal(1, env.GetValue("executedBranches"));
    }

    [Fact]
    public void Evaluate_ElseIfChain_ExecutesElseWhenNoConditionsMatch()
    {
        var env = InterpreterTestHelpers.Run("""
let score = 65;
let grade = 0;

if (score > 90)
{
    grade = 1;
}
else if (score > 80)
{
    grade = 2;
}
else if (score > 70)
{
    grade = 3;
}
else
{
    grade = 4;
}
""");

        Assert.Equal(4, env.GetValue("grade"));
    }

    [Fact]
    public void Evaluate_ElseIfChainWithoutElse_LeavesStateUnchangedWhenNoConditionsMatch()
    {
        var env = InterpreterTestHelpers.Run("""
let score = 65;
let grade = 0;

if (score > 90)
{
    grade = 1;
}
else if (score > 80)
{
    grade = 2;
}
else if (score > 70)
{
    grade = 3;
}
""");

        Assert.Equal(0, env.GetValue("grade"));
    }

    [Fact]
    public void Evaluate_WhileLoop_MutatesStateUntilConditionIsFalse()
    {
        var env = InterpreterTestHelpers.Run("""
let x = 0;
while (x < 5)
{
    x = x + 1;
}
""");

        Assert.Equal(5, env.GetValue("x"));
    }

    [Fact]
    public void Evaluate_NestedLoops_MaintainAccumulatedState()
    {
        var env = InterpreterTestHelpers.Run("""
let totalCycles = 0;
let outer = 0;
while (outer < 3)
{
    let inner = 0;
    while (inner < 4)
    {
        totalCycles = totalCycles + 1;
        inner = inner + 1;
    }
    outer = outer + 1;
}
""");

        Assert.Equal(12, env.GetValue("totalCycles"));
        Assert.Equal(3, env.GetValue("outer"));
    }

    [Fact]
    public void Evaluate_BreakAndContinue_ControlLoopExecution()
    {
        var env = InterpreterTestHelpers.Run("""
let i = 0;
let total = 0;

while (i < 5)
{
    i = i + 1;

    if (i == 2)
    {
        continue;
    }

    if (i == 4)
    {
        break;
    }

    total = total + i;
}
""");

        Assert.Equal(4, env.GetValue("i"));
        Assert.Equal(4, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForeachOverList_VisitsEachValue()
    {
        var env = InterpreterTestHelpers.Run("""
let values = [1, 2, 3];
let total = 0;

foreach (value in values)
{
    total = total + value;
}
""");

        Assert.Equal(6, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForeachOverDictionary_VisitsKeys()
    {
        var env = InterpreterTestHelpers.Run("""
let values = {"first": 2, "second": 3};
let total = 0;

foreach (key in values)
{
    total = total + values[key];
}
""");

        Assert.Equal(5, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForeachBreakAndContinue_ControlLoopExecution()
    {
        var env = InterpreterTestHelpers.Run("""
let values = [1, 2, 3, 4, 5];
let total = 0;

foreach (value in values)
{
    if (value == 2)
    {
        continue;
    }

    if (value == 4)
    {
        break;
    }

    total = total + value;
}
""");

        Assert.Equal(4, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForeachIterator_DoesNotLeakOutsideLoop()
    {
        var env = InterpreterTestHelpers.Run("""
foreach (value in [1])
{
}
""");

        Assert.Throws<Exception>(() => env.GetValue("value"));
    }

    [Fact]
    public void Evaluate_ForLoop_IteratesRange()
    {
        var env = InterpreterTestHelpers.Run("""
let total = 0;
for (i in 0, 10)
{
    total = total + i;
}
""");

        Assert.Equal(45.0, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForLoop_WithStep()
    {
        var env = InterpreterTestHelpers.Run("""
let total = 0;
for (i in 0, 10, 2)
{
    total = total + i;
}
""");

        Assert.Equal(20.0, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForLoop_ReverseStep()
    {
        var env = InterpreterTestHelpers.Run("""
let total = 0;
for (i in 10, 0, -1)
{
    total = total + i;
}
""");

        Assert.Equal(55.0, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForLoop_AutoDescending()
    {
        var env = InterpreterTestHelpers.Run("""
let total = 0;
for (i in 5, 0)
{
    total = total + i;
}
""");

        Assert.Equal(15.0, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForLoop_EmptyRange()
    {
        var env = InterpreterTestHelpers.Run("""
let count = 0;
for (i in 5, 5)
{
    count = count + 1;
}
""");

        Assert.Equal(0, env.GetValue("count"));
    }

    [Fact]
    public void Evaluate_ForLoop_ZeroStep_Throws()
    {
        Assert.Throws<FishboneRuntimeException>(() => InterpreterTestHelpers.Run("""
for (i in 0, 10, 0)
{
}
"""));
    }

    [Fact]
    public void Evaluate_ForLoop_Break()
    {
        var env = InterpreterTestHelpers.Run("""
let total = 0;
for (i in 0, 10)
{
    if (i == 4) { break; }
    total = total + i;
}
""");

        Assert.Equal(6.0, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForLoop_Continue()
    {
        var env = InterpreterTestHelpers.Run("""
let total = 0;
for (i in 0, 5)
{
    if (i == 2) { continue; }
    total = total + i;
}
""");

        Assert.Equal(8.0, env.GetValue("total"));
    }

    [Fact]
    public void Evaluate_ForLoop_IteratorDoesNotLeak()
    {
        var env = InterpreterTestHelpers.Run("""
for (i in 0, 1)
{
}
""");

        Assert.Throws<Exception>(() => env.GetValue("i"));
    }

    [Fact]
    public void Evaluate_ForLoop_NestedLoops()
    {
        var env = InterpreterTestHelpers.Run("""
let total = 0;
for (outer in 0, 3)
{
    for (inner in 0, 4)
    {
        total = total + 1;
    }
}
""");

        Assert.Equal(12, env.GetValue("total"));
    }
}