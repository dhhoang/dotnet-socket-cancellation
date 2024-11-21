using System.Threading;

namespace HuyHoang.EchoClient;

public sealed class RequestCounter
{
    private long requestCount;

    public long RequestCount => Interlocked.Read(ref this.requestCount);

    public void Increment() => Interlocked.Increment(ref this.requestCount);
}
