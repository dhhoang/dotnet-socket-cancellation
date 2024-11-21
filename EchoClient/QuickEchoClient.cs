using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HuyHoang.EchoClient;

public class QuickEchoClient : IDisposable
{
    private readonly int targetDelayMs;
    private readonly byte[] _receiveBuffer = new byte[1024];
    private readonly byte[] _sendBuffer = "Hello, World!\r\n"u8.ToArray();
    private readonly Socket socket;
    private readonly PeriodicTimer periodicTimer;
    private readonly ILogger logger;
    private readonly RequestCounter requestCounter;
    private readonly IPEndPoint endpoint;

    public QuickEchoClient(ILogger logger, RequestCounter requestCounter, IPEndPoint endpoint, int rps)
    {
        this.targetDelayMs = 1000 / rps;
        this.socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        this.periodicTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(this.targetDelayMs));
        this.logger = logger;
        this.requestCounter = requestCounter;
        this.endpoint = endpoint;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await this.socket.ConnectAsync(this.endpoint);
        this.logger.LogWarning("Connected to {Endpoint}", this.endpoint);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await this.periodicTimer.WaitForNextTickAsync(cancellationToken);

                await SendAllAsync(this.socket, this._sendBuffer, cancellationToken);

                var recvBuf = this._receiveBuffer.AsMemory()[..this._sendBuffer.Length];

                await ReceiveAllAsync(this.socket, recvBuf, cancellationToken);

                if (this.logger.IsEnabled(LogLevel.Information))
                {
                    this.logger.LogInformation("Received: {0}", Encoding.UTF8.GetString(recvBuf.Span));
                }

                requestCounter.Increment();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            this.logger.LogError("Error {ex}", ex);
        }
    }

    public void Dispose()
    {
        this.socket.Dispose();
    }

    private static async ValueTask SendAllAsync(Socket socket, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        do
        {
            var result = await socket.SendAsync(data, cancellationToken);
            data = data[result..];
        }
        while (data.Length > 0);
    }

    private static async ValueTask ReceiveAllAsync(Socket socket, Memory<byte> receiveBuffer, CancellationToken cancellationToken)
    {
        do
        {
            var result = await socket.ReceiveAsync(receiveBuffer, cancellationToken);
            receiveBuffer = receiveBuffer[result..];
        }
        while (receiveBuffer.Length > 0);
    }
}
