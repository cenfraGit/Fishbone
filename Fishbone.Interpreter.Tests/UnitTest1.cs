using Fishbone.Core;
using Fishbone.Parser;
using Fishbone.Interpreter;

namespace Fishbone.Interpreter.Tests;

public class UnitTest1
{
    [Fact]
    public void Interpreter_WhileLoop_CorrectlyMutatesState()
    {
        string code = @"
let x = 0;
while (x < 5)
{
    x = x + 1;
}
";

        var ast = ASTParser.Parse(code);
        // ast.PrintAST();

        var env = new FishboneEnvironment();
        var interpreter = new FishboneInterpreter();

        interpreter.Evaluate(env, ast);
        Assert.Equal(5, env.GetValue("x"));
    }

    [Fact]
    public void Interpreter_OperatorPrecedence_EvaluatesInCorrectOrder()
    {
        string code = @"let result = 10 + 3 * 4 > -2 == true;";

        var ast = ASTParser.Parse(code);
        // ast.PrintAST();

        var env = new FishboneEnvironment();
        var interpreter = new FishboneInterpreter();

        interpreter.Evaluate(env, ast);
        Assert.Equal(true, env.GetValue("result"));
    }

    [Fact]
    public void Interpreter_If_ExecutesCorrectPath()
    {
        string code = @"
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
";

        var ast = ASTParser.Parse(code);
        // ast.PrintAST();

        var env = new FishboneEnvironment();
        var interpreter = new FishboneInterpreter();

        interpreter.Evaluate(env, ast);

        Assert.Equal(1, env.GetValue("grade"));
    }

    [Fact]
    public void Interpreter_UnaryInversionAndTruthiness_EvaluatesNatively()
    {
        string code = @"
let basicTrue = true;
let invertedTrue = not basicTrue;
let validZero = 0;
let zeroIsInverted = not validZero;
";

        var ast = ASTParser.Parse(code);
        // ast.PrintAST();

        var env = new FishboneEnvironment();
        var interpreter = new FishboneInterpreter();

        interpreter.Evaluate(env, ast);

        Assert.Equal(false, env.GetValue("invertedTrue"));
        Assert.Equal(true, env.GetValue("zeroIsInverted"));
    }

    [Fact]
    public void Interpreter_NestedLoops_AccuratelyMaintainsAccumulatedState()
    {
        string code = @"
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
";

        var ast = ASTParser.Parse(code);
        // ast.PrintAST();

        var env = new FishboneEnvironment();
        var interpreter = new FishboneInterpreter();

        interpreter.Evaluate(env, ast);

        Assert.Equal(12, env.GetValue("totalCycles"));
        Assert.Equal(3, env.GetValue("outer"));
    }
}
