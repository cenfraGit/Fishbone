using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using Fishbone;
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
        // at global scope "Visible Variables" adds nothing over "Locals", so it is deduplicated away
        Assert.DoesNotContain(scopes, scope => scope.Name == "Visible Variables");
        var visible = scopes.Single(scope => scope.Name == "Locals");
        var variable = Assert.Single(handles.GetVariables(visible.VariablesReference));
        var entry = Assert.Single(handles.GetVariables(variable.VariablesReference));
        var children = handles.GetVariables(entry.VariablesReference);

        Assert.Equal("""{"items": [1, "two"]}""", variable.Value);
        Assert.Equal(["[0]", "[1]"], children.Select(child => child.Name));
        Assert.Equal("two", children[1].Value);
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

    [Theory]
    [MemberData(nameof(DisplayCases))]
    public void FormatsTypesAndValuesLikeANormalRun(object? value, string type, string display)
    {
        // the same text the variable explorer shows after a run without the debugger
        var handles = new DebugSnapshotHandles();
        handles.SetSnapshot(Snapshot(new DebugVariableSnapshot("value", value)));
        long scopeReference = handles.GetScopes(handles.GetFrames()[0].Id)[0].VariablesReference;

        var variable = Assert.Single(handles.GetVariables(scopeReference));

        Assert.Equal(type, variable.Type);
        Assert.Equal(display, variable.Value);
    }

    public static TheoryData<object?, string, string> DisplayCases() => new()
    {
        { new List<object?> { 1, "two" }, "List", """[1, "two"]""" },
        { new Dictionary<object, object?> { ["a"] = 1 }, "Dictionary", """{"a": 1}""" },
        { new KeyValuePair<string, int>("a", 1), "KeyValuePair", "[a, 1]" },
        { "hi", "String", "hi" },
        { 2.5, "Double", "2.5" },
        { true, "Boolean", "true" },
        { null, "null", "null" },
    };

    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static (DebugSnapshotHandles Handles, Variable Variable) ShowVariable(FishboneConfiguration? config, object? value)
    {
        var handles = new DebugSnapshotHandles(config);
        handles.SetSnapshot(Snapshot(new DebugVariableSnapshot("value", value)));
        long scopeReference = handles.GetScopes(handles.GetFrames()[0].Id)[0].VariablesReference;
        return (handles, Assert.Single(handles.GetVariables(scopeReference)));
    }

    private static FishboneConfiguration PictureConfig() =>
        new FishboneConfiguration().AddVisualizer<Picture>(picture => new FishboneImage(1, 1, 1, [picture.Level]));

    [Fact]
    public void ImageValue_HasAReferenceThatGivesItsPng()
    {
        var (handles, variable) = ShowVariable(PictureConfig(), new Picture(7));

        Assert.NotEqual(0, variable.VariablesReference);
        Assert.Equal(PngSignature, handles.GetImagePng(variable.VariablesReference)[..8]);
    }

    [Fact]
    public void ImageValue_WithoutAVisualizer_HasNoReference()
    {
        var (_, variable) = ShowVariable(null, new Picture(7));

        Assert.Equal(0, variable.VariablesReference);
    }

    [Fact]
    public void GetImagePng_RejectsReferencesThatArentImages()
    {
        var (handles, variable) = ShowVariable(PictureConfig(), new List<object?> { 1 });

        Assert.Throws<InvalidOperationException>(() => handles.GetImagePng(variable.VariablesReference));
        Assert.Throws<InvalidOperationException>(() => handles.GetImagePng(12345));
    }

    [Fact]
    public void GetImagePng_RejectsStaleReferences()
    {
        var (handles, variable) = ShowVariable(PictureConfig(), new Picture(7));

        handles.Clear();

        Assert.Throws<InvalidOperationException>(() => handles.GetImagePng(variable.VariablesReference));
    }

    [Fact]
    public void GetImagePng_VisualizerFailure_IsReportedWithItsMessage()
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(_ => throw new InvalidOperationException("no pixels"));
        var (handles, variable) = ShowVariable(config, new Picture(7));

        var exception = Assert.Throws<InvalidOperationException>(() => handles.GetImagePng(variable.VariablesReference));
        Assert.Contains("no pixels", exception.Message);
    }

    private sealed class Picture(byte level)
    {
        public byte Level { get; } = level;
    }

    private static DebugPauseSnapshot Snapshot(DebugVariableSnapshot variable) => new(
        new DebugSourceLocation("test.fb", 1, 1),
        DebugPauseReason.Breakpoint,
        [variable],
        [new DebugCallFrameSnapshot("<script>", new DebugSourceLocation("test.fb", 1, 1), [variable])],
        null);
}