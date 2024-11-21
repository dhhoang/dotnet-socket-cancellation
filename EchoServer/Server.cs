using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HuyHoang.DotnetSocketCancelllation;

public class Server : IDisposable, ISessionFactory<Session>
{
    private readonly Socket listenSocket;
    private readonly ILogger logger;
    private readonly bool useSaea;

    public Server(ILogger logger, EndPoint listenEndpoint, bool useSaea)
    {
        this.listenSocket = CreateAndBindSocket(listenEndpoint);
        this.logger = logger;
        this.useSaea = useSaea;
        this.ServerTask = Task.CompletedTask;
    }

    public Task ServerTask { get; private set; }

    public void Start()
    {
        this.listenSocket.Listen();
        this.logger.LogWarning("Server started: {Endpoint}, useSaea: {UseSaea}", this.listenSocket.LocalEndPoint, this.useSaea);

        this.ServerTask = AcceptLoop(CancellationToken.None);
    }

    public void OnSessionEnd(ISession session)
    {
        session.Dispose();
    }

    public async ValueTask<TSession?> AcceptAsync<TSession>(ISessionFactory<TSession> sessionFactory, CancellationToken cancellationToken)
    where TSession : class, ISession
    {
        Socket? acceptSocket = null;
        do
        {
            try
            {
                acceptSocket = await this.listenSocket.AcceptAsync(cancellationToken);
                if (acceptSocket.RemoteEndPoint is EndPoint ep)
                {
                    this.logger.LogWarning("Accepted connection from {EndPoint}", ep);
                }
            }
            catch (OperationCanceledException)
            {
                // occurs when we are listening and the server shuts down
                return null;
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.OperationAborted)
            {
                // a call was made to Unbind, just return null which signals we're done
                return null;
            }
            catch (ObjectDisposedException)
            {
                // this also means socket is closed 
                return null;
            }
            catch (SocketException sockErr)
            {
                // other error, it might be that a socket in the queue has reset it connection due to timeout, retry
                this.logger.LogError("Error accepting connection {SockErr}", sockErr);
            }
        }
        while (acceptSocket is null);

        return sessionFactory.CreateSession(acceptSocket, ownsSocket: true);
    }

    private async Task AcceptLoop(CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            ISession? newSession = null;
            try
            {
                newSession = await this.AcceptAsync(this, stopToken);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError("Accept error {Exception}", ex);
            }

            if (newSession is null)
            {
                // this means the server is no longer listening due to shutdown
                break;
            }

            // dispatch this session to execute on the threadpool
            ThreadPool.UnsafeQueueUserWorkItem(newSession, preferLocal: false);
        }
    }

    internal static Socket CreateAndBindSocket(EndPoint endPoint)
    {
        Socket listenSocket;
        switch (endPoint)
        {
            case IPEndPoint ipEndpoint:
                listenSocket = new Socket(ipEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                // TODO verify whether we need to enable dual-stack sockets
                if (ipEndpoint.Address.Equals(IPAddress.IPv6Any))
                {
                    listenSocket.DualMode = true;
                }

                break;
            case UnixDomainSocketEndPoint uSock:
                // this is mostly for testing
                listenSocket = new Socket(uSock.AddressFamily, SocketType.Stream, ProtocolType.Unspecified);
                break;
            default:
                // this scenario is for "DnsEndpoint"
                listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                break;
        }

        listenSocket.Bind(endPoint);
        return listenSocket;
    }

    public void Dispose()
    {
        this.listenSocket.Dispose();
    }

    public Session CreateSession(Socket socket, bool ownsSocket)
    {
        return this.useSaea
            ? new SAEASession(this.logger, this, socket, ownsSocket)
            : new CancellationTokenSession(this.logger, this, socket, ownsSocket);
    }
}
