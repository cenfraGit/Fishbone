using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpineIDE.Services;

internal sealed class ScriptInputRequest : IDisposable
{
    private readonly TaskCompletionSource<string> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _cancellationRegistration;

    internal ScriptInputRequest(CancellationToken cancellationToken)
    {
        _cancellationRegistration = cancellationToken.Register(
            () => _completion.TrySetCanceled(cancellationToken));
    }

    internal string Wait()
    {
        return _completion.Task.GetAwaiter().GetResult();
    }

    internal void Submit(string value)
    {
        _completion.TrySetResult(value);
    }

    internal void Cancel()
    {
        _completion.TrySetException(new OperationCanceledException("Script input was cancelled."));
    }

    internal void Fail(Exception exception)
    {
        _completion.TrySetException(exception);
    }

    public void Dispose()
    {
        _cancellationRegistration.Dispose();
    }
}