namespace Fishbone.DebugAdapter;

/// <summary>
/// Wraps the DAP transport stream and completes <see cref="Disconnected"/> the instant the
/// protocol library's own reader observes the peer going away, a read of a non-empty buffer
/// returning 0 (end of stream) or a transport exception. Because this is the single authoritative
/// reader on the socket, it cannot race the protocol library the way a side-channel that polls
/// <c>Socket.Available</c> alongside it does (that approach reports a busy connection as closed
/// when the protocol reader drains the buffer between the poll and the availability check).
/// </summary>
internal sealed class DisconnectSignalingStream(Stream inner) : Stream
{
    private readonly TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the underlying reader sees end-of-stream or fails.</summary>
    public Task Disconnected => _disconnected.Task;

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        try
        {
            read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch when (Signal())
        {
            throw;
        }
        if (read == 0 && buffer.Length > 0)
            _disconnected.TrySetResult();
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int read;
        try
        {
            read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        }
        catch when (Signal())
        {
            throw;
        }
        if (read == 0 && count > 0)
            _disconnected.TrySetResult();
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read;
        try
        {
            read = inner.Read(buffer, offset, count);
        }
        catch when (Signal())
        {
            throw;
        }
        if (read == 0 && count > 0)
            _disconnected.TrySetResult();
        return read;
    }

    // Sets the signal from an exception filter and returns false so the original exception still
    // propagates unchanged (the filter is a side effect, not a handler).
    private bool Signal()
    {
        _disconnected.TrySetResult();
        return false;
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(buffer, cancellationToken);
}