using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HuyHoang.DotnetSocketCancelllation;

internal class Program
{
    private static readonly string? UseSaeaOpt = Environment.GetEnvironmentVariable("ECHOSERVER_USE_SAEA");

    private static IPEndPoint Endpoint = new IPEndPoint(IPAddress.Any, 8087);

    public static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddSimpleConsole(consoleLog =>
            {
                consoleLog.SingleLine = true;
            });
        });

        var logger = loggerFactory.CreateLogger("Server");
        var server = new Server(logger, Endpoint,
            useSaea: "true".Equals(UseSaeaOpt, StringComparison.OrdinalIgnoreCase) ||
                    "1".Equals(UseSaeaOpt, StringComparison.OrdinalIgnoreCase));

        server.Start();

        await server.ServerTask;
    }
}
