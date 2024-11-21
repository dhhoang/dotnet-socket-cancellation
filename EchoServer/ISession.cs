using System;
using System.Net.Sockets;
using System.Threading;

namespace HuyHoang.DotnetSocketCancelllation;

/// <summary>
/// Represents a client session.
/// </summary>
public interface ISession : IThreadPoolWorkItem, IDisposable
{
}


public interface ISessionFactory<TSession>
    where TSession : class, ISession
{
    TSession CreateSession(Socket socket, bool ownsSocket);
}
