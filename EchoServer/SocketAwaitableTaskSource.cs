using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace HuyHoang.DotnetSocketCancelllation;

public readonly struct TransportResult
{
    public readonly int BytesTransferred;

    public readonly SocketError SockErr = SocketError.Success;

    public TransportResult(int bytes, SocketError error)
    {
        this.BytesTransferred = bytes;
        this.SockErr = error;
    }
}

public abstract class SocketAwaitableTaskSource : SocketAsyncEventArgs, IValueTaskSource<TransportResult>
{
    private static readonly Action<object?> ContinuationCompleted = _ => { };

    private volatile Action<object?>? continuation;

    public TransportResult GetResult(short token)
    {
        this.continuation = null;
        return new TransportResult(this.BytesTransferred, this.SocketError);
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        ValueTaskSourceStatus status = !ReferenceEquals(this.continuation, ContinuationCompleted)
                ? ValueTaskSourceStatus.Pending
                : this.SocketError == SocketError.Success
                    ? ValueTaskSourceStatus.Succeeded
                    : ValueTaskSourceStatus.Faulted;
        return status;
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        this.UserToken = state;
        Action<object?>? prevContinuation = Interlocked.CompareExchange<Action<object?>?>(ref this.continuation, continuation, null);

        if (ReferenceEquals(prevContinuation, ContinuationCompleted))
        {
            this.UserToken = null;
            InvokeContinuation(continuation, state);
        }
    }

    protected override void OnCompleted(SocketAsyncEventArgs e)
    {
        Action<object?>? c = this.continuation;

        if (c != null || (c = Interlocked.CompareExchange(ref this.continuation, ContinuationCompleted, null)) != null)
        {
            object? continuationState = this.UserToken;
            this.UserToken = null;
            this.continuation = ContinuationCompleted;

            // TODO queueing this to thread-pool might not be the most efficient way
            InvokeContinuation(c, continuationState);
        }
    }

    private static void InvokeContinuation(Action<object?> continuation, object? state)
        => ThreadPool.UnsafeQueueUserWorkItem(continuation, state, preferLocal: true);
}

public sealed class SocketReceiver : SocketAwaitableTaskSource
{
    public ValueTask<TransportResult> ReceiveAsync(Socket socket, Memory<byte> buffer)
    {
        this.SetBuffer(buffer);

        if (socket.ReceiveAsync(this))
        {
            return new ValueTask<TransportResult>(this, 0);
        }

        return ValueTask.FromResult(new TransportResult(this.BytesTransferred, this.SocketError));
    }
}

public sealed class SocketSender : SocketAwaitableTaskSource
{
    public ValueTask<TransportResult> SendAsync(Socket socket, ReadOnlyMemory<byte> memory)
    {
        this.SetBuffer(MemoryMarshal.AsMemory(memory));

        if (socket.SendAsync(this))
        {
            return new ValueTask<TransportResult>(this, 0);
        }

        return ValueTask.FromResult(new TransportResult(this.BytesTransferred, this.SocketError));
    }
}
