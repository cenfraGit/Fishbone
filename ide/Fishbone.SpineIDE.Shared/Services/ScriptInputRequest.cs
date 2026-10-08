using System;
using System.Threading;
using System.Threading.Tasks;

namespace SpineIDE.Services;

public sealed class ScriptInputRequest : IDisposable
{
    private readonly TaskCompletionSource<string> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _cancellationRegistration;

    public ScriptInputRequest(CancellationToken cancellationToken)
    {
        _cancellationRegistration = cancellationToken.Register(
            () => _completion.TrySetCanceled(cancellationToken));
    }

    public string Wait()
    {
        return _completion.Task.GetAwaiter().GetResult();
    }

    public void Submit(string value)
    {
        _completion.TrySetResult(value);
    }

    public void Cancel()
    {
        _completion.TrySetException(new OperationCanceledException("Script input was cancelled."));
    }

    public void Fail(Exception exception)
    {
        _completion.TrySetException(exception);
    }

    public void Dispose()
    {
        _cancellationRegistration.Dispose();
    }
}