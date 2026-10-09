using Fishbone.Debugging;

namespace Fishbone.Tests;

/// <summary>
/// Step into, over and out across script function calls, and when a runtime error pauses.
/// These drive the real interpreter through a real <see cref="BreakpointCoordinator"/>.
/// </summary>
public class DebuggerSteppingTests
{
    private const string Script = """
func twice(n) {
    let r = n * 2;
    return r;
}
let a = twice(5);
let b = a + 1;
""";

    // runs the script and records every pause. each pause runs the next command, and once
    // the commands run out it continues
    private static List<DebugPauseSnapshot> Pauses(string source, int[] breakpoints, params Action<BreakpointCoordinator>[] commands)
    {
        using var coordinator = new BreakpointCoordinator("test.fb");
        foreach (var line in breakpoints)
            coordinator.AddBreakpoint(line);

        var pauses = new List<DebugPauseSnapshot>();
        coordinator.Paused += (_, args) =>
        {
            pauses.Add(args.Snapshot);
            if (pauses.Count > 20)
                coordinator.Stop();
            else if (pauses.Count <= commands.Length)
                commands[pauses.Count - 1](coordinator);
            else
                coordinator.Continue();
        };

        FishboneProgram.Run(source, new FishboneConfiguration(), coordinator);
        return pauses;
    }

    [Fact]
    public void StepInto_ScriptCall_LandsOnTheFunctionsFirstStatement_AndStepOutReturnsToTheCaller()
    {
        var pauses = Pauses(Script, [5], c => c.StepInto(), c => c.StepOut());

        Assert.Equal(3, pauses.Count);
        Assert.Equal((5, "<script>"), (pauses[0].Location.Line, pauses[0].CallStack[0].FunctionName));

        Assert.Equal(DebugPauseReason.Step, pauses[1].Reason);
        Assert.Equal(2, pauses[1].Location.Line);
        Assert.Equal(["twice", "<script>"], pauses[1].CallStack.Select(frame => frame.FunctionName));
        Assert.Equal(5, pauses[1].CallStack[1].Location.Line);

        // the debugger only stops at statements, so once the call's statement finishes the
        // caller's next statement is where it lands, with the result already assigned
        Assert.Equal(DebugPauseReason.Step, pauses[2].Reason);
        Assert.Equal(6, pauses[2].Location.Line);
        Assert.Equal("<script>", Assert.Single(pauses[2].CallStack).FunctionName);
        Assert.Contains(pauses[2].VisibleVariables, variable => variable.Name == "a" && Equals(variable.Value, 10));
    }

    [Fact]
    public void StepOver_CallWhoseFunctionHasABreakpoint_StopsAtThatBreakpoint()
    {
        var pauses = Pauses(Script, [5, 3], c => c.StepOver());

        Assert.Equal([(5, DebugPauseReason.Breakpoint), (3, DebugPauseReason.Breakpoint)],
            pauses.Select(pause => (pause.Location.Line, pause.Reason)));
        Assert.Equal("twice", pauses[1].CallStack[0].FunctionName);
    }

    [Fact]
    public void RuntimeException_PausesOnlyWhenNoScriptCatchHandlesIt()
    {
        const string source = """
let caught = false;
try { throw "handled"; } catch { caught = true; }
let x = null;
x.Anything();
""";
        using var coordinator = new BreakpointCoordinator("test.fb");
        Assert.True(coordinator.PauseOnRuntimeExceptions);
        var pauses = new List<DebugPauseSnapshot>();
        coordinator.Paused += (_, args) =>
        {
            pauses.Add(args.Snapshot);
            coordinator.Continue();
        };

        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run(source, new FishboneConfiguration(), coordinator));

        DebugPauseSnapshot pause = Assert.Single(pauses);
        Assert.Equal(DebugPauseReason.Exception, pause.Reason);
        Assert.Equal(4, pause.Location.Line);
        Assert.NotNull(pause.Exception);
        Assert.Contains(pause.VisibleVariables, variable => variable.Name == "caught" && Equals(variable.Value, true));
    }
}
