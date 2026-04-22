using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ApotheonMod;

/// <summary>
/// Updated NetworkInterface resolver from
/// https://github.com/space-wizards/SpaceWizards.Lidgren.Network/blob/wizards/Lidgren.Network/Platform/PlatformStandard.cs
/// </summary>
internal static class NetworkInterfaceProvider
{
    #region Public Methods

    public static NetworkInterface GetNetworkInterface()
    {
        var defaultAddress = ProbeDefaultRouteAddress();

        return NetworkInterface.GetAllNetworkInterfaces()
                               .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                             nic.NetworkInterfaceType != NetworkInterfaceType.Unknown &&
                                             nic.Supports(NetworkInterfaceComponent.IPv4))
                               .OrderByDescending(nic =>
                               {
                                   if (nic.OperationalStatus != OperationalStatus.Up)
                                       return 0;

                                   // Try to get an adapter with a proper MAC address.
                                   // This means it will ignore things like certain VPN tunnels.
                                   // Also, peer UIDs are generated based off MAC address so not getting an empty one is probably good.
                                   if (nic.GetPhysicalAddress().GetAddressBytes().Length == 0)
                                       return 1;

                                   foreach (var address in nic.GetIPProperties().UnicastAddresses)
                                   {
                                       // If this is the adapter for the default address, it wins hands down. 
                                       if (defaultAddress != null && address.Address.Equals(defaultAddress))
                                           return 4;

                                       // make sure this adapter has any ipv4 addresses
                                       if (address is { Address: { AddressFamily: AddressFamily.InterNetwork } })
                                           return 3;
                                   }

                                   return 2;
                               })
                               .FirstOrDefault();
    }

    #endregion

    #region Non-Public Methods

    private static IPAddress ProbeDefaultRouteAddress()
    {
        try
        {
            // Try to infer the default network interface by "connecting" a UDP socket to a global IP address.
            // This will not send any real data (like with TCP), but it *will* cause the OS
            // to fill in the local address of the socket with the local address of the interface that would be used,
            // based on the OS routing tables.
            // This basically gets us the network interface address "that goes to the router".
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPAddress(new byte[]
            {
                1, 1, 1, 1
            }), 12345);
            return ((IPEndPoint)socket.LocalEndPoint!).Address;
        }
        catch
        {
            // I can't imagine why this would fail but if it does let's just uh...
            return null;
        }
    }

    #endregion
}
