using System;
using System.Net;

namespace ApotheonMasterServer;

public class ApotheonMasterServer
{
    #region Fields

    private static MessageHandler? messageHandler;

    #endregion

    #region Public Methods

    public static void Main(string[] args)
    {
        var logger = new Logger();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestShutdown();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            RequestShutdown();
        };

        var port = LoadEnvPort();
        messageHandler = new MessageHandler(logger, port);
        messageHandler.Run();
    }

    #endregion

    #region Non-Public Methods

    private static int LoadEnvPort()
    {
        const string MASTER_SERVER_PORT_KEY = "MASTER_SERVER_PORT";

        return int.TryParse(Environment.GetEnvironmentVariable(MASTER_SERVER_PORT_KEY), out var port) ? port : 14343;
    }

    private static void RequestShutdown()
    {
        messageHandler?.KeepAlive = false;
    }

    #endregion

    #region Nested Types

    public record ServerInfo(
        string IPAddress,
        int Port,
        long Id,
        string Name,
        string Map,
        int Players,
        int MaxPlayers,
        int Bots,
        int GameMode,
        int WeaponMode,
        int Ping);

    internal record HostEntry(long Id, IPEndPoint InternalIP, IPEndPoint ExternalIP, ServerInfo Info, DateTime LastSeen);

    #endregion
}
