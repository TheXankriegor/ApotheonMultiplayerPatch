using System.Net;

namespace ApotheonMasterServer;

internal static class IpHelper
{
    #region Public Methods

    public static bool TryParseEndpoint(string externalIp, out IPEndPoint? endPoint)
    {
        endPoint = null;

        if (string.IsNullOrWhiteSpace(externalIp))
            return false;

        var ip = externalIp.Trim();
        var port = 14242;

        if (ip.Contains(':'))
        {
            var idx = ip.IndexOf(':');
            port = int.Parse(ip[(idx + 1)..]);
            ip = ip[..idx];
        }

        if (!IPAddress.TryParse(ip, out var address))
            return false;

        endPoint = new IPEndPoint(address, port);
        return true;
    }

    #endregion
}
