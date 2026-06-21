using Fishbone.Debugging;

namespace Fishbone.DebugAdapter.Tests;

public class DebugSnapshotHandlesTests
{
    [Fact]
    public void ExposesFramesScopesAndExpandableCollections()
    {
        var list = new List<object?> { 1, "two" };
        var dictionary = new Dictionary<object, object?> { ["items"] = list };
        var snapshot = new DebugPauseSnapshot(
            new DebugSourceLocation("test.fb", 4, 1),
            DebugPauseReason.Breakpoint,
            [new DebugVariableSnapshot("values", dictionary)],
            [new DebugCallFrameSnapshot("<script>", new DebugSourceLocation("test.fb", 4, 1), [new DebugVariableSnapshot("values", dictionary)])],
            null);
        var handles = new DebugSnapshotHandles();

        handles.SetSnapshot(snapshot);
        var frame = Assert.Single(handles.GetFrames());
        var scopes = handles.GetScopes(frame.Id);
        var visible = scopes.Single(scope => scope.Name == "Visible Variables");
        var variable = Assert.Single(handles.GetVariables(visible.VariablesReference));
        var entry = Assert.Single(handles.GetVariables(variable.VariablesReference));
        var children = handles.GetVariables(entry.VariablesReference);

        Assert.Equal("dictionary (1)", variable.Value);
        Assert.Equal(["[0]", "[1]"], children.Select(child => child.Name));
        Assert.Equal("\"two\"", children[1].Value);
    }

    [Fact]
    public void ReusesCollectionHandlesForCyclesAndRejectsStaleReferences()
    {
        var values = new List<object?>();
        values.Add(values);
        var snapshot = Snapshot(new DebugVariableSnapshot("values", values));
        var handles = new DebugSnapshotHandles();
        handles.SetSnapshot(snapshot);
        long scopeReference = handles.GetScopes(handles.GetFrames()[0].Id)[0].VariablesReference;
        long collectionReference = handles.GetVariables(scopeReference)[0].VariablesReference;

        var child = Assert.Single(handles.GetVariables(collectionReference));
        Assert.Equal(collectionReference, child.VariablesReference);

        handles.Clear();
        Assert.Throws<InvalidOperationException>(() => handles.GetVariables(collectionReference));
    }

    private static DebugPauseSnapshot Snapshot(DebugVariableSnapshot variable) => new(
        new DebugSourceLocation("test.fb", 1, 1),
        DebugPauseReason.Breakpoint,
        [variable],
        [new DebugCallFrameSnapshot("<script>", new DebugSourceLocation("test.fb", 1, 1), [variable])],
        null);
}