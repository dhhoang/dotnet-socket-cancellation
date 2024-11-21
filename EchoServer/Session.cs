using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HuyHoang.DotnetSocketCancelllation;

public abstract class Session : ISession
{
    private const int BufferBlockSize = 4096;
    private const int MaxBufferSize = 8192;

    private readonly Server server;
    private readonly Socket socket;
    private readonly bool ownsSocket;
    private readonly MemoryPool<byte> bufferPool;
    private readonly Pipe responsePipe = new Pipe();
    private readonly ILogger logger;


    private IMemoryOwner<byte> bufferOwner;
    private bool disposedValue;

    public Session(ILogger logger, Server server, Socket socket, bool ownsSocket)
    {
        this.server = server;
        this.socket = socket;
        this.ownsSocket = ownsSocket;
        this.bufferPool = MemoryPool<byte>.Shared;
        this.logger = logger;
        this.bufferOwner = this.bufferPool.Rent(1024);
    }

    public void Execute()
    {
        _ = ExecuteAsync();
    }

    protected abstract ValueTask<TransportResult> RecvAsync(Socket socket, Memory<byte> buffer);

    protected abstract ValueTask<TransportResult> SendAsync(Socket socket, ReadOnlyMemory<byte> buffer);

    private async Task ExecuteAsync()
    {
        Exception? ioException = null;

        var responseTask = this.ResponseLoop();

        try
        {
            int startIdx = 0, len = 0;
            while (ioException == null)
            {
                var consumed = await this.HandleInputAsync(bufferOwner.Memory.Slice(startIdx, len), this.responsePipe.Writer);
                startIdx += consumed;
                len -= consumed;

                // ensure we have buffer space
                this.AdjustBuffer(ref startIdx, ref len);

                // read from the connection
                TransportResult readResult = await this.RecvAsync(this.socket, this.bufferOwner.Memory.Slice(startIdx + len));
                // TransportResult readResult = await socketReceiver.ReceiveAsync(this.socket, this.bufferOwner.Memory.Slice(startIdx + len));

                if (readResult.SockErr != SocketError.Success)
                {
                    ioException = new SocketException((int)readResult.SockErr);
                    break;
                }

                if (readResult.BytesTransferred == 0)
                {
                    break;
                }

                len += readResult.BytesTransferred;
            }
        }
        catch (Exception ex)
        {
            this.responsePipe.Reader.CancelPendingRead();
            ioException = ex;
        }
        finally
        {
            this.responsePipe.Writer.Complete();

            if (ioException is Exception ioError)
            {
                this.logger.LogInformation("Session error {Exception}", ioError);
            }

            this.server.OnSessionEnd(this);
        }
    }

    private async ValueTask<int> HandleInputAsync(ReadOnlyMemory<byte> buffer, PipeWriter pipeWriter)
    {
        int consumed = 0;

        while (true)
        {
            int lfIdx = buffer.Span.IndexOf((byte)'\n');
            if (lfIdx < 1 || buffer.Span[lfIdx - 1] != (byte)'\r')
            {
                break;
            }

            consumed += lfIdx + 1;
            ReadOnlyMemory<byte> line = buffer.Slice(0, lfIdx + 1);
            buffer = buffer.Slice(lfIdx + 1);

            // echo
            await pipeWriter.WriteAsync(line);
        }

        return consumed;
    }

    private void AdjustBuffer(ref int startIdx, ref int len)
    {
        // TODO this is naive just to do prototype
        if (len == 0)
        {
            startIdx = 0;
        }

        // check if we can't read anymore
        if (startIdx + len == this.bufferOwner.Memory.Length)
        {
            if (startIdx > 0 && startIdx >= this.bufferOwner.Memory.Length / 2)
            {
                // reuse the first half
                this.bufferOwner.Memory.Slice(startIdx, len).CopyTo(this.bufferOwner.Memory);
                startIdx = 0;
            }
            else
            {
                // allocate more buffers
                IMemoryOwner<byte> oldBuffer = this.bufferOwner;
                int newBufSize = this.bufferOwner.Memory.Length + BufferBlockSize;
                if (newBufSize <= MaxBufferSize)
                {
                    this.bufferOwner = this.bufferPool.Rent(newBufSize);

                    // copy
                    oldBuffer.Memory.Slice(startIdx, len).CopyTo(this.bufferOwner.Memory);
                    startIdx = 0;
                    oldBuffer.Dispose();
                }
            }
        }
    }

    private async Task ResponseLoop()
    {
        SocketError sendErr = SocketError.Success;

        try
        {
            while (true)
            {
                ReadResult readResult = await this.responsePipe.Reader.ReadAsync();

                if (readResult.IsCanceled)
                {
                    // someone call CancelPendingRead(), we are aborting
                    break;
                }

                ReadOnlySequence<byte> buffer = readResult.Buffer;

                sendErr = await WriteDataSequence(buffer);
                if (sendErr != SocketError.Success)
                {
                    throw new SocketException((int)sendErr);
                }

                this.responsePipe.Reader.AdvanceTo(buffer.End);

                if (readResult.IsCompleted)
                {
                    // end of response stream
                    break;
                }
            }
        }
        finally
        {
            this.responsePipe.Reader.Complete();

            // if the protocol handler is pending a flush, cancel it because we are not processing anymore
            this.responsePipe.Writer.CancelPendingFlush();
        }
    }

    private async ValueTask<SocketError> WriteDataSequence(ReadOnlySequence<byte> buffer)
    {
        SocketError sendErr = SocketError.Success;

        if (buffer.IsEmpty)
        {
            return sendErr;
        }

        if (buffer.IsSingleSegment)
        {
            // fast path
            sendErr = await this.SendAllAsync(buffer.First);
        }
        else
        {
            ReadOnlySequence<byte>.Enumerator enumerator = buffer.GetEnumerator();
            while (enumerator.MoveNext())
            {
                sendErr = await SendAllAsync(enumerator.Current);
                if (sendErr != SocketError.Success)
                {
                    break;
                }
            }
        }

        return sendErr;
    }

    private async ValueTask<SocketError> SendAllAsync(ReadOnlyMemory<byte> data)
    {
        do
        {
            TransportResult result = await this.SendAsync(this.socket, data);
            if (result.SockErr != SocketError.Success)
            {
                return result.SockErr;
            }

            data = data[result.BytesTransferred..];
        }
        while (data.Length > 0);

        return SocketError.Success;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                if (this.ownsSocket)
                {
                    this.socket.Dispose();
                }
                this.logger.LogWarning("Session disposed");
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
