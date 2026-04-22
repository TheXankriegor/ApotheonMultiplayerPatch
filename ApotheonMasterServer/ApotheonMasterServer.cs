using System;
using System.Net;
using System.Text.Json;
using System.Threading;

using Lidgren.Network;

namespace ApotheonMasterServer;

public class ApotheonMasterServer
{
    #region Enums

    private enum MessageType : byte
    {
        PacketRegister = 0,
        PacketQuit = 1,
        PacketNatIntroRequest = 3,
        PacketListRequest = 4,
        PacketDiagPing = 240,
        PacketDiagStatusRequest = 241,
        PacketDiagPong = 242,
        PacketDiagStatusResponse = 243
    }

    #endregion

    #region Fields

    private static Logger logger;
    private static int masterServerPort;
    private static bool keepAlive;

    #endregion

    #region Public Methods

    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RequestShutdown();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            RequestShutdown();
        };

        logger = new Logger();

        LoadEnvSettings();
        LaunchServer();
    }

    #endregion

    #region Non-Public Methods

    private static void LoadEnvSettings()
    {
        const string masterServerPortKey = "MASTER_SERVER_PORT";

        masterServerPort = int.TryParse(Environment.GetEnvironmentVariable(masterServerPortKey), out var port) ? port : 14343;
    }

    private static void Run(NetPeerConfiguration peerConfig)
    {
        NetPeer? peer = null;

        try
        {
            peer = new NetPeer(peerConfig);
            peer.Start();

            while (keepAlive)
            {
                try
                {
                    ExpireHosts();
                    ProcessMessages(peer);
                }
                catch (Exception ex)
                {
                    logger.Error($"Server execution encountered an error: {ex}");
                }

                Thread.Sleep(10);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Server execution encountered a critical error: {ex}");
        }
        finally
        {
            if (peer is not null)
            {
                try
                {
                    peer.Shutdown("Server shutting down");
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to shut down server: {ex}");
                }
            }
        }
    }

    private static void ProcessMessages(NetPeer peer)
    {
        NetIncomingMessage msg;

        while ((msg = peer!.ReadMessage()) is not null)
        {
            try
            {
                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.UnconnectedData:
                        HandleUnconnected(msg);
                        break;

                    case NetIncomingMessageType.StatusChanged:
                    case NetIncomingMessageType.ErrorMessage:
                    case NetIncomingMessageType.WarningMessage:
                    case NetIncomingMessageType.DebugMessage:
                    case NetIncomingMessageType.VerboseDebugMessage:
                        logger.Info("");
                        break;

                    default:
                        logger.Warning($"Received unexpected message type '{msg.MessageType}'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"The server ran into an error while reading a message: {ex.Message}");
            }
            finally
            {
                peer!.Recycle(msg);
            }
        }
    }

    private static void OnRegister(NetIncomingMessage msg)
    {
        var sender = msg.SenderEndPoint;
        var reportedInternal = msg.ReadIPEndPoint();
        var id = msg.ReadInt64();
        var json = msg.ReadString();
        //var reportedExternalRaw = TryReadTrailingString(msg);
        //var reportedExternal = TryParseEndPoint(reportedExternalRaw, sender.Port);
        //var effectiveInternal = Sanitize(reportedInternal, sender, id);
        //var effectiveExternal = reportedExternal ?? sender;
        //var extraBytes = msg.LengthBytes - msg.PositionInBytes;
        //bool isNew = !hosts.ContainsKey(id);
        //var info = ParseServerInfo(json);

        //hosts[id] = new HostEntry(id, effectiveInternal, effectiveExternal, json, info, DateTime.UtcNow);

        //if (isNew)
        //{
        //    logger.Info($"Adding server: [{id}]  ", $"Server ID: {id}", $"Name: {DisplayOrUnknown(info.Name, "Unknown server")}",
        //                $"Map: {DisplayOrUnknown(info.Map, "Unknown map")}", $"Players: {info.Players}/{info.MaxPlayers}",
        //                $"External IP: {MasterLog.Describe(effectiveExternal)}", $"Reported LAN IP: {MasterLog.Describe(effectiveInternal)}");
        //}
    }

    private static void HandleUnconnected(NetIncomingMessage msg)
    {
        if (msg.LengthBytes - msg.PositionInBytes < 1)
        {
            logger.Warning("Dropping empty message.");
            return;
        }

        var type = (MessageType)msg.ReadByte();

        logger.Debug($"Received package type={type} sender={msg.SenderEndPoint}");

        switch (type)
        {
            case MessageType.PacketRegister:
                OnRegister(msg);
                break;

            //case MessageType.PacketQuit:
            //    OnQuit(msg);
            //    break;

            //case MessageType.PacketNatIntroRequest:
            //    OnNatIntro(msg);
            //    break;

            //case MessageType.PacketListRequest:
            //    OnListRequest(msg);
            //    break;

            //case MessageType.PacketDiagPing:
            //    OnDiagnosticPing(msg);
            //    break;

            //case MessageType.PacketDiagStatusRequest:
            //    OnDiagnosticStatusRequest(msg);
            //    break;

            default:
                throw new ArgumentOutOfRangeException($"Invalid message type '{type}'.");
        }
    }

    private static void ExpireHosts()
    {
    }

    private static void LaunchServer()
    {
        logger.Info($"Launching Apotheon Master Server on port {masterServerPort}.");

        var peerConfig = new NetPeerConfiguration(nameof(ApotheonMasterServer))
        {
            Port = masterServerPort,
        };

        peerConfig.EnableMessageType(NetIncomingMessageType.UnconnectedData);
        peerConfig.EnableMessageType(NetIncomingMessageType.ErrorMessage);
        peerConfig.EnableMessageType(NetIncomingMessageType.WarningMessage);
        peerConfig.EnableMessageType(NetIncomingMessageType.StatusChanged);

        keepAlive = true;

        Run(peerConfig);
    }

    private static void RequestShutdown()
    {
        keepAlive = true;
    }

    private ParsedServerInfo ParseServerInfo(string json)
    {
        return JsonSerializer.Deserialize<ParsedServerInfo>(json) ?? new ParsedServerInfo("Unknown server", "Unknown map", 0, 0, null);

    }

    #endregion

    #region Nested Types

    private record HostEntry(long Id, IPEndPoint InternalIP, IPEndPoint ExternalIP, string ServerInfoJson, ParsedServerInfo Info, DateTime LastSeen);

    private record ParsedServerInfo(string Name, string Map, int Players, int MaxPlayers, string? AdvertisedIp);

    #endregion
}
