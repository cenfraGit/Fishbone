// --------------------------------------------------------------------------------
// LiveParseScheduler.cs
//
// re-parses the open script on a typing pause and hands the diagnostics back.
//
// deliberately free of any Avalonia dispatcher. the debounce is a cancellable
// Task.Delay rather than a DispatcherTimer, and results are posted back through the
// SynchronizationContext captured at construction. that is what lets the versioning
// and cancellation be tested at all: the ide's test project has no headless Avalonia,
// so a DispatcherTimer would never tick and the interesting logic would ship
// unexercised.
//
// three things guard the parse itself:
//
// - antlr cannot be interrupted once it starts, so a stale parse runs to completion
//   and its result is discarded at the boundary. the version check, not a token, is
//   what keeps a slow result from overwriting a newer one.
// - the parse runs on a thread with a large stack, because deep input overflows and a
//   StackOverflowException would take the ide down. the injected depth check refuses
//   the worst input before it gets here; the big stack covers what that check's crude
//   character counting misses.
// - one parse at a time across the whole process, so several open tabs cannot saturate
//   the machine between keystrokes.
// --------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using Fishbone.Core;

namespace SpineIDE.Views.Editor;

internal sealed class LiveParseScheduler : IDisposable
{
    /// <summary>
    /// How long the typing has to stop before a parse starts. Longer than the completion popup's
    /// 150ms, because a fluent typist's keystrokes land 100-200ms apart: firing inside that would
    /// spend the work on half-typed tokens, whose diagnostics are noise by construction.
    /// </summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Above this, the live parse is skipped. Orders of magnitude beyond any real script; it
    /// exists so no input can make typing feel slow, since a parse allocates roughly 1.5MB per
    /// 1000 characters and that adds up at a typing cadence.
    /// </summary>
    public const int MaxSourceLength = 256 * 1024;

    /// <summary>
    /// Most diagnostics published from one parse. A single missing brace can make ANTLR's error
    /// recovery emit a screenful, and every one costs the renderer a geometry build.
    /// </summary>
    public const int MaxDiagnostics = 50;

    // 16MB against the default 1MB, which buys roughly an order of magnitude of nesting depth
    private const int ParseThreadStackBytes = 16 * 1024 * 1024;

    // one parse at a time process-wide; also keeps concurrent pressure off the parser
    private static readonly SemaphoreSlim ParseSlot = new(1, 1);

    private readonly TimeSpan _debounce;
    private readonly Func<string, bool> _isTooDeep;
    private readonly Func<string, IReadOnlyList<FishboneDiagnostic>> _parse;
    private readonly Action<IReadOnlyList<FishboneDiagnostic>> _publish;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SynchronizationContext? _context;

    private CancellationTokenSource? _pending;
    private int _version;
    private bool _disposed;

    public LiveParseScheduler(
        Func<string, IReadOnlyList<FishboneDiagnostic>> parse,
        Action<IReadOnlyList<FishboneDiagnostic>> publish,
        Func<string, bool>? isTooDeep = null,
        TimeSpan? debounce = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _parse = parse;
        _isTooDeep = isTooDeep ?? (_ => false);
        _publish = publish;
        _debounce = debounce ?? DefaultDebounce;
        _delay = delay ?? Task.Delay;
        _context = SynchronizationContext.Current;
    }

    /// <summary>
    /// Requests a parse of <paramref name="snapshot"/> once the typing pauses, superseding any
    /// request still waiting. Call from the UI thread: the snapshot has to be taken there, and the
    /// version counter is only safe because nothing else touches it.
    /// </summary>
    public void Schedule(ITextSource snapshot)
    {
        if (_disposed)
            return;

        int version = ++_version;

        _pending?.Cancel();
        _pending?.Dispose();
        var cancellation = new CancellationTokenSource();
        _pending = cancellation;

        _ = RunAsync(snapshot, version, cancellation.Token);
    }

    /// <summary>Abandons any pending parse and stops accepting new ones.</summary>
    public void Dispose()
    {
        _disposed = true;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }

    private async Task RunAsync(ITextSource snapshot, int version, CancellationToken cancellation)
    {
        try
        {
            // cancelling this delay is the debounce: a burst of keystrokes leaves a burst of
            // immediately-cancelled waits behind it and only the last one gets through
            await _delay(_debounce, cancellation).ConfigureAwait(false);

            if (cancellation.IsCancellationRequested || IsStale(version))
                return;

            await ParseSlot.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                // re-checked after queueing: a slow parse ahead of this one may have let several
                // newer requests pile up, and running them all would be pure waste
                if (IsStale(version))
                    return;

                string text = snapshot.Text;
                IReadOnlyList<FishboneDiagnostic> diagnostics =
                    text.Length > MaxSourceLength || _isTooDeep(text)
                        ? []
                        : await ParseOnDeepStackAsync(text).ConfigureAwait(false);

                Publish(version, Cap(diagnostics));
            }
            finally
            {
                ParseSlot.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke, which is the normal outcome
        }
    }

    // ANTLR offers no way to bail out mid-parse, so this cannot be cancelled; the caller drops
    // the result instead. the dedicated thread is purely for its stack size
    private Task<IReadOnlyList<FishboneDiagnostic>> ParseOnDeepStackAsync(string text)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<FishboneDiagnostic>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(_parse(text));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, ParseThreadStackBytes) { IsBackground = true, Name = "fishbone-live-parse" };

        thread.Start();
        return completion.Task;
    }

    private static IReadOnlyList<FishboneDiagnostic> Cap(IReadOnlyList<FishboneDiagnostic> diagnostics) =>
        diagnostics.Count <= MaxDiagnostics ? diagnostics : [.. diagnostics.Take(MaxDiagnostics)];

    private bool IsStale(int version) => _disposed || version != Volatile.Read(ref _version);

    private void Publish(int version, IReadOnlyList<FishboneDiagnostic> diagnostics)
    {
        // the staleness check has to happen where the version is written, not on the worker
        void Apply()
        {
            if (!IsStale(version))
                _publish(diagnostics);
        }

        if (_context is null)
            Apply();
        else
            _context.Post(_ => Apply(), null);
    }
}