using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;

using Lidgren.Network;

using static ApotheonMasterServer.ApotheonMasterServer;

namespace ApotheonMasterServer;

public class MessageHandler
{
    #region Enums

    internal enum MessageType : byte
    {
        PacketRegister = 0,
        PacketQuit = 1,
        PacketNatIntroRequest = 3,
        PacketListRequest = 4
    }

    #endregion

    #region Constants

    private const int Timeout = 30;

    #endregion

    #region Fields

    private readonly NetServer _peer;
    private readonly ILogger _logger;
    private readonly Dictionary<long, HostEntry> _hosts;

    #endregion

    #region Constructors

    public MessageHandler(ILogger logger, int port)
    {
        _logger = logger;
        _hosts = new Dictionary<long, HostEntry>();

        _logger.Info($"Launching Apotheon Master Server on port {port}.");

        var peerConfig = new NetPeerConfiguration(nameof(ApotheonMasterServer))
        {
            Port = port,
        };

        peerConfig.EnableMessageType(NetIncomingMessageType.UnconnectedData);
        peerConfig.EnableMessageType(NetIncomingMessageType.ErrorMessage);
        peerConfig.EnableMessageType(NetIncomingMessageType.WarningMessage);
        peerConfig.EnableMessageType(NetIncomingMessageType.StatusChanged);

        _peer = new NetServer(peerConfig);
        _peer.Start();
        KeepAlive = true;
    }

    #endregion

    #region Properties

    public bool KeepAlive { get; set; }

    #endregion

    #region Non-Public Methods

    /// <summary>
    /// Runs the server loop synchronously (blocks until Stop is called).
    /// </summary>
    internal void Run()
    {
        while (KeepAlive)
        {
            try
            {
                ExpireHosts();
                ProcessMessages();
            }
            catch (Exception ex)
            {
                _logger.Error($"Server execution encountered an error: {ex}");
            }

            Thread.Sleep(10);
        }

        try
        {
            _peer.Shutdown("Server shutting down");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to shut down server: {ex}");
        }
    }

    internal void OnRegister(NetIncomingMessage msg)
    {
        var sender = msg.SenderEndPoint;
        var internalEndpoint = msg.ReadIPEndPoint();
        var id = msg.ReadInt64();
        var json = msg.ReadString();
        var externalIp = msg.ReadString();
        if (!IpHelper.TryParseEndpoint(externalIp, out var externalEndpoint))
            externalEndpoint = sender;

        var info = JsonSerializer.Deserialize<ServerInfo>(json);

        if (info == null || externalEndpoint == null)
        {
            _logger.Warning($"Failed to register server {id} from {sender}");
            return;
        }

        if (!_hosts.ContainsKey(id))
            _logger.Info($"Registering new server {id} from {externalEndpoint}: {info.Name} on {info.Map}");

        _hosts[id] = new HostEntry(id, internalEndpoint, externalEndpoint, info, DateTime.UtcNow);
    }

    internal void OnQuit(NetIncomingMessage msg)
    {
        _ = msg.ReadIPEndPoint();
        var id = msg.ReadInt64();

        if (_hosts.Remove(id, out var removedHost))
            _logger.Info($"Unregistering server {id} from {removedHost.ExternalIP.Address} (Shutdown)");
    }

    internal void OnNatIntro(NetIncomingMessage msg)
    {
        var clientExternal = msg.SenderEndPoint;
        var clientReportedInternal = msg.ReadIPEndPoint();
        var hostId = msg.ReadInt64();
        var token = msg.ReadString();

        _logger.Info($"Player trying to join server {hostId} from {clientExternal} (LAN: {clientReportedInternal})");

        if (!_hosts.TryGetValue(hostId, out var host))
        {
            _logger.Warning($"Server {hostId} not found for NAT introduction");
            return;
        }

        _peer.Introduce(host.InternalIP, host.ExternalIP, clientReportedInternal, clientExternal, token);

        _logger.Info($"NAT introduction completed for server {hostId}: host={host.ExternalIP} (LAN: {host.InternalIP})");
    }

    internal void OnListRequest(NetIncomingMessage msg)
    {
        var client = msg.SenderEndPoint;

        if (_hosts.Count == 0)
        {
            var empty = _peer.CreateMessage();
            empty.Write(false);
            _peer.SendUnconnectedMessage(empty, client);
            return;
        }

        foreach (var host in _hosts.Values)
        {
            var response = _peer.CreateMessage();
            response.Write(true);
            response.Write(host.Id);
            response.Write(host.InternalIP);
            response.Write(host.ExternalIP);
            response.Write(JsonSerializer.Serialize(host.Info));
            response.Write(string.Empty);
            _peer.SendUnconnectedMessage(response, client);
        }
    }

    internal void ExpireHosts()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-Timeout);

        foreach (var id in _hosts.Where(x => x.Value.LastSeen < cutoff).Select(x => x.Key).ToArray())
        {
            if (_hosts.Remove(id, out var removedHost))
                _logger.Info($"Unregistering server {id} from {removedHost.ExternalIP.Address} (Timeout)");
        }
    }

    internal void ProcessMessages()
    {
        while (_peer.ReadMessage() is { } msg)
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
                        _logger.Info($"Received {msg.MessageType}");
                        break;

                    default:
                        _logger.Warning($"Received unexpected message type '{msg.MessageType}'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"The server ran into an error while reading a message: {ex.Message}");
            }
            finally
            {
                _peer.Recycle(msg);
            }
        }
    }

    internal void HandleUnconnected(NetIncomingMessage msg)
    {
        if (msg.LengthBytes - msg.PositionInBytes < 1)
        {
            _logger.Warning("Dropping empty message.");
            return;
        }

        var type = (MessageType)msg.ReadByte();

        _logger.Debug($"Received package type={type} sender={msg.SenderEndPoint}");

        switch (type)
        {
            case MessageType.PacketRegister:
                OnRegister(msg);
                break;

            case MessageType.PacketQuit:
                OnQuit(msg);
                break;

            case MessageType.PacketNatIntroRequest:
                OnNatIntro(msg);
                break;

            case MessageType.PacketListRequest:
                OnListRequest(msg);
                break;

            default:
                _logger.Debug($"Unexpected message type '{type}'.");
                break;
        }
    }

    #endregion
}
