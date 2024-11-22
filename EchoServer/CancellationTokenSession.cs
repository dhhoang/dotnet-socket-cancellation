using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HuyHoang.DotnetSocketCancelllation;

public sealed class CancellationTokenSession : Session
{
    private readonly Socket socket;
    private CancellationTokenSource sendCts = new CancellationTokenSource();
    private CancellationTokenSource recvCts = new CancellationTokenSource();

    // To detect redundant calls
    private bool disposedValue;

    public CancellationTokenSession(ILogger logger, Server server, Socket socket, bool ownsSocket)
        : base(logger, server, socket, ownsSocket)
    {
        this.socket = socket;
    }

    protected override async ValueTask<TransportResult> RecvAsync(Socket socket, Memory<byte> buffer)
    {
        var cts = this.GetRecvCts();
        try
        {
            var bytesReceived = await this.socket.ReceiveAsync(buffer, cts.Token);
            return new TransportResult(bytesReceived, SocketError.Success);
        }
        catch (SocketException sockErr)
        {
            return new TransportResult(0, sockErr.SocketErrorCode);
        }
    }

    protected override async ValueTask<TransportResult> SendAsync(Socket socket, ReadOnlyMemory<byte> buffer)
    {
        var cts = this.GetSendCts();
        try
        {
            var bytesSent = await this.socket.SendAsync(buffer, cts.Token);
            return new TransportResult(bytesSent, SocketError.Success);
        }
        catch (SocketException sockErr)
        {
            return new TransportResult(0, sockErr.SocketErrorCode);
        }
    }

    private CancellationTokenSource GetSendCts()
    {
        CancellationTokenSource resultCts;
        if (!this.sendCts.TryReset())
        {
            this.sendCts.Dispose();
            resultCts = this.sendCts = new CancellationTokenSource();
        }
        else
        {
            resultCts = this.sendCts;
        }

        resultCts.CancelAfter(TimeSpan.FromMinutes(1));
        return resultCts;
    }

    private CancellationTokenSource GetRecvCts()
    {
        CancellationTokenSource resultCts;
        if (!this.recvCts.TryReset())
        {
            this.recvCts.Dispose();
            resultCts = this.recvCts = new CancellationTokenSource();
        }
        else
        {
            resultCts = this.recvCts;
        }

        resultCts.CancelAfter(TimeSpan.FromMinutes(1));
        return resultCts;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                this.sendCts.Dispose();
                this.recvCts.Dispose();
            }

            disposedValue = true;
        }

        // Call base class implementation.
        base.Dispose(disposing);
    }
}
