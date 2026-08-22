using System.Threading;
using AvaloniaEdit.Document;
using Fishbone.Core;
using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

// the scheduler is dispatcher-free precisely so this file can exist: the debounce, the version
// gate and the guards are the parts most likely to break subtly, and a DispatcherTimer would
// never tick here.
public class LiveParseSchedulerTests
{
    private static ITextSource Snapshot(string text) => new TextDocument(text).CreateSnapshot();

    private static FishboneDiagnostic Diagnostic(string message = "boom") =>
        new(DiagnosticStage.Parse, DiagnosticSeverity.Error, message, new SourceSpan(1, 1, 1, 2));

    // an immediate delay collapses the debounce so a test never waits on wall-clock time, while
    // still exercising the cancellation path: a cancelled token must still throw
    private static Task NoDelay(TimeSpan _, CancellationToken token) =>
        token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;

    private static async Task<T> Eventually<T>(Func<T> read, Func<T, bool> until)
    {
        for (int i = 0; i < 200; i++)
        {
            T value = read();
            if (until(value))
                return value;
            await Task.Delay(10);
        }
        return read();
    }

    [Fact]
    public async Task Schedule_ParsesAndPublishes()
    {
        var published = new List<IReadOnlyList<FishboneDiagnostic>>();
        using var scheduler = new LiveParseScheduler(
            _ => [Diagnostic()], published.Add, delay: NoDelay);

        scheduler.Schedule(Snapshot("let x = 1"));

        await Eventually(() => published.Count, n => n > 0);
        Assert.Single(published);
        Assert.Single(published[0]);
    }

    [Fact]
    public async Task Schedule_PassesTheSnapshotText()
    {
        string? seen = null;
        using var scheduler = new LiveParseScheduler(
            text => { seen = text; return []; }, _ => { }, delay: NoDelay);

        scheduler.Schedule(Snapshot("let x = 42;"));

        await Eventually(() => seen, s => s is not null);
        Assert.Equal("let x = 42;", seen);
    }

    [Fact]
    public async Task ABurstOfKeystrokes_ParsesOnlyTheLastOne()
    {
        // the whole point of the debounce. a real Task.Delay is used here because collapsing it
        // would let every request through before the next Schedule could cancel it
        var parsed = new List<string>();
        using var scheduler = new LiveParseScheduler(
            text => { lock (parsed) parsed.Add(text); return []; },
            _ => { },
            debounce: TimeSpan.FromMilliseconds(60));

        for (int i = 1; i <= 6; i++)
            scheduler.Schedule(Snapshot($"let x = {i};"));

        await Task.Delay(400);
        lock (parsed)
        {
            Assert.Single(parsed);
            Assert.Equal("let x = 6;", parsed[0]);
        }
    }

    [Fact]
    public async Task AStaleResult_IsDiscardedRatherThanPublished()
    {
        // ANTLR cannot be stopped mid-parse, so a superseded parse still finishes. it must not
        // overwrite the newer answer, which is what the version gate is for
        var gate = new SemaphoreSlim(0, 1);
        var published = new List<string>();

        using var scheduler = new LiveParseScheduler(
            text =>
            {
                if (text.Contains("slow"))
                    gate.Wait();
                return [Diagnostic(text)];
            },
            diagnostics => { lock (published) published.Add(diagnostics[0].Message); },
            delay: NoDelay);

        scheduler.Schedule(Snapshot("slow"));
        await Task.Delay(50);
        scheduler.Schedule(Snapshot("fresh"));   // supersedes it
        gate.Release();                          // now let the stale parse finish

        await Task.Delay(300);
        lock (published)
            Assert.DoesNotContain("slow", published);
    }

    [Fact]
    public async Task DeepInput_IsNotParsedAtAll()
    {
        // a stack overflow cannot be caught, so the guard has to run before the parser does
        bool parsed = false;
        using var scheduler = new LiveParseScheduler(
            _ => { parsed = true; return []; },
            _ => { },
            isTooDeep: _ => true,
            delay: NoDelay);

        scheduler.Schedule(Snapshot("anything"));

        await Task.Delay(200);
        Assert.False(parsed, "the depth guard should have skipped the parse");
    }

    [Fact]
    public async Task DeepInput_PublishesAnEmptyResultSoOldSquigglesClear()
    {
        var published = new List<IReadOnlyList<FishboneDiagnostic>>();
        using var scheduler = new LiveParseScheduler(
            _ => [Diagnostic()], published.Add, isTooDeep: _ => true, delay: NoDelay);

        scheduler.Schedule(Snapshot("anything"));

        await Eventually(() => published.Count, n => n > 0);
        Assert.Empty(published[0]);
    }

    [Fact]
    public async Task HugeInput_IsNotParsed()
    {
        bool parsed = false;
        using var scheduler = new LiveParseScheduler(
            _ => { parsed = true; return []; }, _ => { }, delay: NoDelay);

        scheduler.Schedule(Snapshot(new string('x', LiveParseScheduler.MaxSourceLength + 1)));

        await Task.Delay(200);
        Assert.False(parsed, "the size guard should have skipped the parse");
    }

    [Fact]
    public async Task ACascadeOfDiagnostics_IsCapped()
    {
        // one missing brace can make ANTLR's recovery emit a screenful, and each one costs the
        // renderer a geometry build
        var published = new List<IReadOnlyList<FishboneDiagnostic>>();
        using var scheduler = new LiveParseScheduler(
            _ => [.. Enumerable.Range(0, 500).Select(i => Diagnostic($"e{i}"))],
            published.Add,
            delay: NoDelay);

        scheduler.Schedule(Snapshot("bad"));

        await Eventually(() => published.Count, n => n > 0);
        Assert.Equal(LiveParseScheduler.MaxDiagnostics, published[0].Count);
    }

    [Fact]
    public async Task Dispose_StopsAPendingParseFromPublishing()
    {
        var published = new List<IReadOnlyList<FishboneDiagnostic>>();
        var scheduler = new LiveParseScheduler(
            _ => [Diagnostic()], published.Add, debounce: TimeSpan.FromMilliseconds(80));

        scheduler.Schedule(Snapshot("let x = 1"));
        scheduler.Dispose();

        await Task.Delay(300);
        Assert.Empty(published);
    }

    [Fact]
    public void Schedule_AfterDispose_IsIgnored()
    {
        bool parsed = false;
        var scheduler = new LiveParseScheduler(_ => { parsed = true; return []; }, _ => { }, delay: NoDelay);

        scheduler.Dispose();
        scheduler.Schedule(Snapshot("let x = 1"));

        Assert.False(parsed);
    }

    [Fact]
    public async Task RealParsing_ReportsASyntaxErrorAndThenClearsIt()
    {
        // end to end against the real engine, which is what the editor actually wires up
        var published = new List<IReadOnlyList<FishboneDiagnostic>>();
        using var scheduler = new LiveParseScheduler(
            Fishbone.Engine.FishboneEngine.Validate,
            published.Add,
            Fishbone.Engine.FishboneEngine.IsTooDeepToValidate,
            delay: NoDelay);

        scheduler.Schedule(Snapshot("let x = 1"));       // missing semicolon
        await Eventually(() => published.Count, n => n > 0);
        Assert.NotEmpty(published[^1]);

        scheduler.Schedule(Snapshot("let x = 1;"));      // fixed
        await Eventually(() => published.Count, n => n > 1);
        Assert.Empty(published[^1]);
    }
}