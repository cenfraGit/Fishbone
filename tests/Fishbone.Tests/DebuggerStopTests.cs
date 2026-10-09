using Fishbone.Debugging;

namespace Fishbone.Tests;

/// <summary>
/// The debugger stops at statements, not at every expression inside them. So a call or list
/// spread over several lines is one stop, and each loop iteration is a new arrival at the
/// lines of its body.
/// </summary>
public class DebuggerStopTests
{
    // runs the script and returns the line of every pause. with step set, it pauses at the
    // first statement and steps over until the end. otherwise it continues from each breakpoint
    private static List<int> PausedLines(string source, bool step, params int[] breakpoints)
    {
        using var coordinator = new BreakpointCoordinator("test.fb");
        foreach (var line in breakpoints)
            coordinator.AddBreakpoint(line);
        if (step)
            coordinator.Pause();

        var lines = new List<int>();
        coordinator.Paused += (_, args) =>
        {
            // stepping off the end pauses once more to show the final state. it isn't a stop
            if (args.Snapshot.Reason != DebugPauseReason.Completed)
                lines.Add(args.Snapshot.Location.Line);
            if (lines.Count > 50)
                coordinator.Stop();
            else if (step)
                coordinator.StepOver();
            else
                coordinator.Continue();
        };

        var config = new FishboneConfiguration()
            .AddBuiltIn("add", new Func<int, int, int>((a, b) => a + b));
        FishboneProgram.Run(source, config, coordinator);
        return lines;
    }

    [Theory]
    [InlineData("""
let n = 0;
foreach (x in [1, 2, 3]) {
    n = n + x;
}
""")]
    [InlineData("""
let n = 0;
for (i in 0, 3) {
    n = n + i;
}
""")]
    [InlineData("""
let n = 0;
while (n < 3) {
    n = n + 1;
}
""")]
    [InlineData("""
let n = 0;
foreach (x in [1, 2, 3]) {
    n = n + x;
    n = n + 1;
}
""")]
    [InlineData("""
let n = 0;
foreach (x in [1, 2, 3])
    n = n + x;
""")]
    public void Run_BreakpointInLoopBody_HitsEveryIteration(string source)
    {
        Assert.Equal([3, 3, 3], PausedLines(source, step: false, 3));
    }

    [Fact]
    public void Run_StepOverForLoop_StopsAtHeaderEachIteration()
    {
        var lines = PausedLines("""
let n = 0;
for (i in 0, 2) {
    n = n + i;
}
let done = true;
""", step: true);

        Assert.Equal([1, 2, 3, 2, 3, 5], lines);
    }

    [Fact]
    public void Run_StepOverMultiLineCall_StopsOnce()
    {
        var lines = PausedLines("""
let r = add(
    1,
    2);
let s = r;
""", step: true);

        Assert.Equal([1, 4], lines);
    }

    [Fact]
    public void Run_StepOverMultiLineList_StopsOnce()
    {
        var lines = PausedLines("""
let values = [
    1,
    2
];
let n = 0;
""", step: true);

        Assert.Equal([1, 5], lines);
    }

    [Fact]
    public void Run_StepOverMultiLineCallStatement_StopsOnce()
    {
        // a call on its own is a statement too, even though it has no node of its own
        var lines = PausedLines("""
add(
    1,
    2);
let n = 0;
""", step: true);

        Assert.Equal([1, 4], lines);
    }

    [Fact]
    public void Run_StepOverElseIf_StopsAtEachBranchHeader()
    {
        var lines = PausedLines("""
let x = 2;
if (x == 1) {
    x = 10;
} else if (x == 2) {
    x = 20;
}
let done = true;
""", step: true);

        Assert.Equal([1, 2, 4, 5, 7], lines);
    }

    [Fact]
    public void Run_StepOverIfWithoutBraces_StopsAtBody()
    {
        var lines = PausedLines("""
let x = 1;
if (x == 1)
    x = 2;
let done = true;
""", step: true);

        Assert.Equal([1, 2, 3, 4], lines);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_PauseAtEnd_ShowsTheFinalValuesWithoutStepping(bool pauseAtEnd)
    {
        // a debug session that runs to the end still shows where it finished, when asked to
        using var coordinator = new BreakpointCoordinator("test.fb") { PauseAtEnd = pauseAtEnd };
        var pauses = new List<(DebugPauseReason Reason, object? X)>();
        coordinator.Paused += (_, args) =>
        {
            object? x = args.Snapshot.VisibleVariables.FirstOrDefault(variable => variable.Name == "x")?.Value;
            pauses.Add((args.Snapshot.Reason, x));
            coordinator.Continue();
        };

        FishboneProgram.Run("let x = 1;\nx = 2;", new FishboneConfiguration(), coordinator);

        if (pauseAtEnd)
            Assert.Equal([(DebugPauseReason.Completed, (object?)2)], pauses);
        else
            Assert.Empty(pauses);
    }
}
