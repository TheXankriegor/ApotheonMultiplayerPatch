using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;

using BepInEx;
using BepInEx.NET.Common;

using HarmonyLib;

namespace ApotheonMod;

[BepInPlugin("548c0700-d8f2-439b-8e11-3dd47a3ecce6", ModName, Version)]
public class ApotheonPlugin : BasePlugin
{
    #region Constants

    private const string Version = "0.0.2";
    private const string ModName = "Apotheon Multiplayer Patch";
    private const string OriginalMasterServer = "50.19.227.23";

    #endregion

    #region Fields

    private static Type apotheonGameType;

    private static string masterServer;
    private static bool loaded;
    private static NetworkInterface cachedInterface;
    private static ApotheonPlugin instance;

    #endregion

    #region Public Methods

    public override void Load()
    {
        instance = this;

        Log.LogInfo($"Initializing {ModName} v{Version}");

        apotheonGameType = AccessTools.TypeByName("Apotheon.Play.ApotheonGame");

        HarmonyInstance.Patch(AccessTools.Method(apotheonGameType, "NetworkUpdate"),
                              prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixNetworkUpdate)));

        var serverBrowserType = AccessTools.TypeByName("Apotheon.ServerBrowser");

        HarmonyInstance.Patch(AccessTools.Method(serverBrowserType, "OnInitialize"),
                              prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixNetworkUpdate)));
    }

    #endregion

    #region Non-Public Methods

    private static void PrefixNetworkUpdate()
    {
        if (loaded)
            return;

        try
        {
            InitializeSettings();

            var netUtilityType = AccessTools.TypeByName("Lidgren.Network.NetUtility");

            instance.HarmonyInstance.Patch(AccessTools.Method(netUtilityType, "Resolve", new[]
            {
                typeof(string)
            }), prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixResolve)));
            instance.HarmonyInstance.Patch(AccessTools.Method(netUtilityType, "GetNetworkInterface"),
                                           postfix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PostfixGetNetworkInterface)));

            instance.HarmonyInstance.Patch(AccessTools.Constructor(apotheonGameType, new[]
            {
                typeof(string), typeof(bool), typeof(string)
            }), prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixDirectJoinConstructor)));

            instance.Log.LogInfo($"Patched methods.");
            loaded = true;
        }
        catch (Exception ex)
        {
            instance.Log.LogError(ex);
        }
    }

    private static long GetAddress(string ipAddress)
    {
        // create 64-bit buffer
        var bytes = new byte[8];

        // copy ip address bytes to buffer
        IPAddress.Parse(ipAddress).GetAddressBytes().CopyTo(bytes, 0);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return BitConverter.ToInt64(bytes, 0);
    }

    private static bool PrefixDirectJoinConstructor(ref string IPAddress)
    {
        try
        {
            instance.Log.LogInfo($"Direct join detected to: {IPAddress}");

            var ip = IPAddress;
            var port = 14242;

            if (IPAddress.Contains(":"))
            {
                var idx = IPAddress.IndexOf(":", StringComparison.Ordinal);
                port = int.Parse(IPAddress.Substring(idx + 1));
                ip = IPAddress.Substring(0, idx);
            }

            var newIp = $"{GetAddress(ip)}";
            if (port != 14242)
                newIp += $":{port}";

            instance.Log.LogInfo($"Updated {IPAddress} to: {newIp}");

            IPAddress = newIp;
        }
        catch (Exception ex)
        {
            instance.Log.LogError(ex);
        }

        return true;
    }

    private static void InitializeSettings()
    {
        var basePath = Path.Combine(Assembly.GetExecutingAssembly().Location, "..");

        var settingsFile = Path.Combine(basePath, "settings.cfg");

        if (!File.Exists(settingsFile))
        {
            var masterServerTemplate = new[]
            {
                "# Apotheon and Apotheon Arena master server override", "#",
                "# This file contains an alternative master server address to use instead of the hardcoded one.",
                "# The first line without '#' will be used as the alternative address.",
                "# For hosting your own master server see https://github.com/cybervand/ApothArena-MasterServer", "#", $"{OriginalMasterServer}"
            };

            File.WriteAllText(settingsFile, string.Join("\n", masterServerTemplate));
        }

        foreach (var raw in File.ReadAllLines(settingsFile))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.InvariantCultureIgnoreCase))
                continue;

            masterServer = line;
            break;
        }

        instance.Log.LogInfo($"Using master server from config: {masterServer}");
    }

    private static void PostfixGetNetworkInterface(ref NetworkInterface __result)
    {
        try
        {
            instance.Log.LogDebug($"Modifying retrieved NetworkInterface.");

            cachedInterface ??= NetworkInterfaceProvider.GetNetworkInterface();

            __result = cachedInterface;
        }
        catch (Exception ex)
        {
            instance.Log.LogError(ex);
        }
    }

    private static bool PrefixResolve(ref string ipOrHost)
    {
        if (ipOrHost != OriginalMasterServer)
            return true;

        instance.Log.LogDebug($"Changing master server ip to '{masterServer}'.");
        ipOrHost = masterServer;

        return true;
    }

    #endregion
}
