// --------------------------------------------------------------------------------
// DeepStackRunner.cs
//
// runs a piece of work on a thread with a much larger stack than the default.
//
// both the generated parser and AstBuilderVisitor recurse over the expression rule,
// so how deeply nested a script can be before it dies is decided by the stack of
// whatever thread happens to be parsing. on the default 1MB that limit is low
// enough to reach by accident: about 500 chained binary operators, roughly 2KB of
// text. and a StackOverflowException cannot be caught, so hitting it takes the whole
// process down with everything unsaved in it.
//
// the thread is per call. creating one costs tens of microseconds against a parse
// measured in milliseconds, and reusing a worker would mean owning a queue and its
// lifetime for no real gain.
// --------------------------------------------------------------------------------

using System.Runtime.ExceptionServices;

namespace Fishbone.Parser;

internal static class DeepStackRunner
{
    // 16MB against a default of 1MB, which buys roughly an order of magnitude of nesting
    private const int StackBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Runs <paramref name="work"/> on a deep-stacked thread and waits for it. Blocking and
    /// synchronous, which is what the parse entry points already are. An exception is rethrown
    /// on the calling thread with its original type and stack trace intact.
    /// </summary>
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception exception)
                {
                    // captured rather than rethrown here: an exception escaping a thread's entry
                    // point would tear the process down instead of reaching the caller
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            },
            StackBytes)
        {
            IsBackground = true,
            Name = "fishbone-parse"
        };

        thread.Start();
        thread.Join();

        failure?.Throw();
        return result;
    }
}