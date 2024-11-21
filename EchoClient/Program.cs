using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HuyHoang.EchoClient;
using Microsoft.Extensions.Logging;

namespace EchoClient;

internal static class Program
{
    private static RequestCounter requestCounter = new RequestCounter();

    public static async Task<int> Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            // set this to Information for debugging
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddSimpleConsole(consoleLog =>
            {
                consoleLog.SingleLine = true;
            });
        });
        var logger = loggerFactory.CreateLogger("Client");

        if (args.Length < 3)
        {
            logger.LogError("Usage: EchoClient <ip>:<port> <clientCount> <rpsPerClient>");
            return 1;
        }

        var endpoint = IPEndPoint.Parse(args[0]);
        var clientCount = int.Parse(args[1]);
        var rpsPerClient = int.Parse(args[2]);

        var tasks = new Task[clientCount];
        for (var i = 0; i < clientCount; i++)
        {
            tasks[i] = RunClient(logger, endpoint, rpsPerClient);
        }

        await Tracker(logger, default);
        await Task.WhenAll(tasks);

        return 0;
    }

    private static async Task RunClient(ILogger logger, IPEndPoint endPoint, int rps)
    {
        using var client = new QuickEchoClient(logger, requestCounter, endPoint, rps);
        await client.RunAsync(default);
    }

    private static async Task Tracker(ILogger logger, CancellationToken stopToken)
    {
        var startTime = Stopwatch.GetTimestamp();
        var startCounter = requestCounter.RequestCount;
        while (!stopToken.IsCancellationRequested)
        {
            await Task.Delay(2000);
            var now = Stopwatch.GetTimestamp();
            var elapsed = now - startTime;
            startTime = now;
            var newCounter = requestCounter.RequestCount;
            var delta = newCounter - startCounter;
            startCounter = newCounter;

            var rate = (int)Math.Round(delta * 1.0 * Stopwatch.Frequency / elapsed);
            logger.LogWarning("Rate: {Rate}", rate);
        }
    }
}
