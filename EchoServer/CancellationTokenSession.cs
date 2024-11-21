using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HuyHoang.DotnetSocketCancelllation;

public sealed class CancellationTokenSession : Session
{
    private readonly Socket socket;
    private CancellationTokenSource cts = new CancellationTokenSource();
    // To detect redundant calls
    private bool disposedValue;

    public CancellationTokenSession(ILogger logger, Server server, Socket socket, bool ownsSocket)
        : base(logger, server, socket, ownsSocket)
    {
        this.socket = socket;
    }

    protected override async ValueTask<TransportResult> RecvAsync(Socket socket, Memory<byte> buffer)
    {
        var cts = this.GetCts();
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
        var cts = this.GetCts();
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

    private CancellationTokenSource GetCts()
    {
        CancellationTokenSource resultCts;
        if (!this.cts.TryReset())
        {
            this.cts.Dispose();
            resultCts = this.cts = new CancellationTokenSource();
        }
        else
        {
            resultCts = this.cts;
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
                this.cts.Dispose();
            }

            disposedValue = true;
        }

        // Call base class implementation.
        base.Dispose(disposing);
    }
}
