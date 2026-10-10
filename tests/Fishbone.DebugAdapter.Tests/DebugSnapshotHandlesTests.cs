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
    public void ImageValue_HasAReferenceThatGivesItsImage()
    {
        var (handles, variable) = ShowVariable(PictureConfig(), new Picture(7));

        Assert.NotEqual(0, variable.VariablesReference);
        Assert.Equal([7], handles.GetImage(variable.VariablesReference).Pixels);
    }

    [Fact]
    public void ImageResponse_CarriesThePngAndTheShapes()
    {
        var region = new FishboneRegion([0], [0], [1]);
        var image = new FishboneImage(2, 1, 1, [7, 7]) { Regions = [region] };

        var response = FishboneImageResponse.From(image);

        Assert.Equal(PngSignature, Convert.FromBase64String(response.Png)[..8]);
        Assert.Equal((2, 1), (response.Width, response.Height));
        Assert.Same(region, Assert.Single(response.Regions));
        Assert.Empty(response.Contours);
    }

    [Fact]
    public void ImageResponse_ForOnlyShapes_HasNoPng()
    {
        var contour = new FishboneContour([0, 4], [0, 9]);

        var response = FishboneImageResponse.From(FishboneImage.FromShapes([], [contour]));

        Assert.Equal("", response.Png);
        Assert.Equal((10, 5), (response.Width, response.Height));
        Assert.Same(contour, Assert.Single(response.Contours));
    }

    [Fact]
    public void ImageValue_WithoutAVisualizer_HasNoReference()
    {
        var (_, variable) = ShowVariable(null, new Picture(7));

        Assert.Equal(0, variable.VariablesReference);
    }

    [Fact]
    public void GetImage_RejectsReferencesThatArentImages()
    {
        var (handles, variable) = ShowVariable(PictureConfig(), new List<object?> { 1 });

        Assert.Throws<InvalidOperationException>(() => handles.GetImage(variable.VariablesReference));
        Assert.Throws<InvalidOperationException>(() => handles.GetImage(12345));
    }

    [Fact]
    public void GetImage_RejectsStaleReferences()
    {
        var (handles, variable) = ShowVariable(PictureConfig(), new Picture(7));

        handles.Clear();

        Assert.Throws<InvalidOperationException>(() => handles.GetImage(variable.VariablesReference));
    }

    [Fact]
    public void GetImage_VisualizerFailure_IsReportedWithItsMessage()
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(_ => throw new InvalidOperationException("no pixels"));
        var (handles, variable) = ShowVariable(config, new Picture(7));

        var exception = Assert.Throws<InvalidOperationException>(() => handles.GetImage(variable.VariablesReference));
        Assert.Contains("no pixels", exception.Message);
    }

    // a stack holds several frames, and each frame is an image of its own
    private static FishboneConfiguration StackConfig() => new FishboneConfiguration()
        .AddVisualizer<Frame>(frame => new FishboneImage(1, 1, 1, [frame.Level]))
        .AddVisualizer<Stack>(stack => new FishboneImage(1, 1, 1, [stack.Levels[0]]),
            children: stack => stack.Levels.Length < 2 ? null : stack.Levels.Select((level, index) => ($"[{index + 1}]", (object?)new Frame(level))).ToArray());

    [Fact]
    public void ImageHoldingSeveral_HasOneImageChildPerImage()
    {
        var (handles, variable) = ShowVariable(StackConfig(), new Stack([10, 20, 30]));

        Assert.Equal(DebugSnapshotHandles.ImageKind, variable.PresentationHint?.Kind);
        Assert.Equal(3, variable.IndexedVariables);
        // the same reference gives the first image, and the children
        Assert.Equal([10], handles.GetImage(variable.VariablesReference).Pixels);
        var children = handles.GetVariables(variable.VariablesReference);
        Assert.Equal(["[1]", "[2]", "[3]"], children.Select(child => child.Name));
        Assert.All(children, child => Assert.Equal(DebugSnapshotHandles.ImageKind, child.PresentationHint?.Kind));
        Assert.Equal([20], handles.GetImage(children[1].VariablesReference).Pixels);
    }

    [Fact]
    public void ImageHoldingOne_HasNoChildren()
    {
        var (handles, variable) = ShowVariable(StackConfig(), new Stack([10]));

        Assert.Null(variable.IndexedVariables);
        Assert.Empty(handles.GetVariables(variable.VariablesReference));
    }

    [Fact]
    public void ImageChildren_AreDisposedWhenThePauseEnds()
    {
        var (handles, variable) = ShowVariable(StackConfig(), new Stack([10, 20]));
        var frames = Frame.Made.TakeLast(2).ToArray();
        // asked once, however often the children are listed
        handles.GetVariables(variable.VariablesReference);
        handles.GetVariables(variable.VariablesReference);
        Assert.All(frames, frame => Assert.False(frame.Disposed));

        handles.Clear();

        Assert.All(frames, frame => Assert.True(frame.Disposed));
    }

    [Fact]
    public void ImageChildrenThatFail_LeaveASingleImage()
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(picture => new FishboneImage(1, 1, 1, [picture.Level]),
            children: _ => throw new InvalidOperationException("no frames"));
        var (handles, variable) = ShowVariable(config, new Picture(7));

        Assert.Null(variable.IndexedVariables);
        Assert.Equal([7], handles.GetImage(variable.VariablesReference).Pixels);
    }

    private sealed class Picture(byte level)
    {
        public byte Level { get; } = level;
    }

    private sealed class Stack(byte[] levels)
    {
        public byte[] Levels { get; } = levels;
    }

    private sealed class Frame : IDisposable
    {
        public static readonly List<Frame> Made = [];

        public Frame(byte level)
        {
            Level = level;
            lock (Made)
                Made.Add(this);
        }

        public byte Level { get; }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static DebugPauseSnapshot Snapshot(DebugVariableSnapshot variable) => new(
        new DebugSourceLocation("test.fb", 1, 1),
        DebugPauseReason.Breakpoint,
        [variable],
        [new DebugCallFrameSnapshot("<script>", new DebugSourceLocation("test.fb", 1, 1), [variable])],
        null);
}