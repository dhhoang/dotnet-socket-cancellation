using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;

namespace HuyHoang.DotnetSocketCancelllation;

public class ServerClientSockets : IDisposable
{
    public required Socket ListenSocket { get; init; }

    public required Socket ClientSocket { get; init; }

    public required Socket ServerSocket { get; init; }

    public static async Task<ServerClientSockets> CreateAsync(string unixSockPath)
    {
        var endpoint = new UnixDomainSocketEndPoint(unixSockPath);
        var listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            listenSocket.Bind(endpoint);
            listenSocket.Listen(128);

            var clientSock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            try
            {
                var connectTask = clientSock.ConnectAsync(new UnixDomainSocketEndPoint(unixSockPath));
                var serverSock = await listenSocket.AcceptAsync();
                await connectTask;

                return new ServerClientSockets
                {
                    ListenSocket = listenSocket,
                    ServerSocket = serverSock,
                    ClientSocket = clientSock
                };
            }
            catch
            {
                clientSock.Dispose();
                throw;
            }
        }
        catch
        {
            listenSocket.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        this.ListenSocket.Dispose();
        this.ClientSocket.Dispose();
        this.ServerSocket.Dispose();
    }
}

[HardwareCounters(
    HardwareCounter.BranchMispredictions,
    HardwareCounter.BranchInstructions)]
[MemoryDiagnoser]
public class SendVsCancellationToken
{
    private const int ByteCount = 1024 * 1024;
    private static readonly string unixSockPath = Path.Combine(Path.GetTempPath(), "dotnet-sock-cancellable.sock");
    private static readonly byte[] sendBuffer = Enumerable.Repeat('a', ByteCount).Select(c => (byte)c).ToArray();
    private static readonly byte[] receiveBuffer = new byte[1];

    private ServerClientSockets? sockets;
    private static readonly SocketSender socketSender = new SocketSender();
    private static readonly SocketReceiver socketReceiver = new SocketReceiver();

    private CancellationTokenSource cts = new CancellationTokenSource();

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        this.sockets = await ServerClientSockets.CreateAsync(unixSockPath);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        var clientSock = this.sockets!.ClientSocket;
        SendAllAsync(clientSock, sendBuffer, default).AsTask().Wait();
    }

    [Benchmark]
    public async Task UseSAEA()
    {
        // var clientSock = this.sockets!.ClientSocket;
        var serverSock = this.sockets!.ServerSocket;

        // await socketSender.SendAsync(clientSock, sendBuffer);
        for (var i = 0; i < ByteCount; i++)
        {
            await socketReceiver.ReceiveAsync(serverSock, receiveBuffer);
        }
    }

    [Benchmark]
    public async Task UseCancellationToken()
    {
        var cts = GetCancellationTokenSource();
        // var clientSock = this.sockets!.ClientSocket;
        var serverSock = this.sockets!.ServerSocket;

        // await clientSock.SendAsync(sendBuffer, cts.Token);
        for (var i = 0; i < ByteCount; i++)
        {
            await serverSock.ReceiveAsync(receiveBuffer, cts.Token);
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        this.sockets?.Dispose();
        if (File.Exists(unixSockPath))
        {
            File.Delete(unixSockPath);
        }
    }

    private CancellationTokenSource GetCancellationTokenSource()
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

    private static async ValueTask SendAllAsync(SocketSender sender, Socket socket, ReadOnlyMemory<byte> data)
    {
        do
        {
            TransportResult result = await sender.SendAsync(socket, data);
            data = data[result.BytesTransferred..];
        }
        while (data.Length > 0);
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

    private static async ValueTask ReceiveAllAsync(SocketReceiver receiver, Socket socket, Memory<byte> receiveBuffer)
    {
        do
        {
            TransportResult result = await receiver.ReceiveAsync(socket, receiveBuffer);
            receiveBuffer = receiveBuffer[result.BytesTransferred..];
        }
        while (receiveBuffer.Length > 0);
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
