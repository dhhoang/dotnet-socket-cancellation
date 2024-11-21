using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HuyHoang.DotnetSocketCancelllation;

public sealed class SAEASession : Session
{
    private readonly SocketSender socketSender = new SocketSender();
    private readonly SocketReceiver socketReceiver = new SocketReceiver();

    // To detect redundant calls
    private bool disposedValue;

    public SAEASession(ILogger logger, Server server, Socket socket, bool ownsSocket)
        : base(logger, server, socket, ownsSocket)
    {
    }

    protected override ValueTask<TransportResult> RecvAsync(Socket socket, Memory<byte> buffer)
        => this.socketReceiver.ReceiveAsync(socket, buffer);

    protected override ValueTask<TransportResult> SendAsync(Socket socket, ReadOnlyMemory<byte> buffer)
        => this.socketSender.SendAsync(socket, buffer);

    protected override void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                this.socketReceiver.Dispose();
                this.socketSender.Dispose();
            }

            disposedValue = true;
        }

        // Call base class implementation.
        base.Dispose(disposing);
    }
}
