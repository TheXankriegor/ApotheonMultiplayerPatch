using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Reflection.Emit;

using BepInEx;
using BepInEx.NET.Common;

using HarmonyLib;

namespace ApotheonMod;

[BepInPlugin("548c0700-d8f2-439b-8e11-3dd47a3ecce6", ModName, Version)]
public class ApotheonPlugin : BasePlugin
{
    #region Constants

    private const string Version = "1.0.0";
    private const string ModName = "Apotheon Multiplayer Patch";
    private const string OriginalMasterServer = "50.19.227.23";
    private const string DefaultCustomMasterServer = "138.2.150.186";

    #endregion

    #region Fields

    private static ReflectedType netUtilityType;
    private static ReflectedType netUPnPType;
    private static ReflectedType netClientType;
    private static ReflectedType hostInfoType;
    private static ReflectedType netPeerConfigurationType;
    private static ReflectedType networkType;
    private static ReflectedType apotheonGameType;

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

        apotheonGameType = new ReflectedType("Apotheon.Play.ApotheonGame");

        HarmonyInstance.Patch(apotheonGameType.Method("NetworkUpdate"), prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixNetworkUpdate)));

        var serverBrowserType = new ReflectedType("Apotheon.ServerBrowser");

        HarmonyInstance.Patch(serverBrowserType.Method("OnInitialize"), prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixNetworkUpdate)));
    }

    #endregion

    #region Non-Public Methods

    private static void ReflectApotheonTypes()
    {
        networkType = new ReflectedType("Apotheon.Network");
        hostInfoType = new ReflectedType("Apotheon.HostInfo");
        netUtilityType = new ReflectedType("Lidgren.Network.NetUtility");

        netPeerConfigurationType = new ReflectedType("Lidgren.Network.NetPeerConfiguration");
        netClientType = new ReflectedType("Lidgren.Network.NetClient");
        netUPnPType = new ReflectedType("Lidgren.Network.NetUPnP");
    }

    private static void PrefixNetworkUpdate()
    {
        if (loaded)
            return;

        try
        {
            ReflectApotheonTypes();
            InitializeSettings();

            instance.HarmonyInstance.Patch(netUtilityType.Method("Resolve", new[]
            {
                typeof(string)
            }), prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixResolve)));
            instance.HarmonyInstance.Patch(netUtilityType.Method("GetNetworkInterface"),
                                           postfix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PostfixGetNetworkInterface)));

            instance.HarmonyInstance.Patch(AccessTools.Constructor(apotheonGameType.Type, new[]
            {
                typeof(string), typeof(bool), typeof(string)
            }), prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixDirectJoinConstructor)));

            instance.HarmonyInstance.Patch(AccessTools.Constructor(apotheonGameType.Type, new[]
            {
                typeof(string), typeof(bool), typeof(string)
            }), prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixDirectJoinConstructor)));

            // patches to update ApotheonArena Networking to match Apotheon main game
            if (AccessTools.AllAssemblies().Any(x => x.GetName().Name == "ApotheonArena"))
            {
                instance.HarmonyInstance.Patch(networkType.Method("ClientStart"),
                                               prefix: new HarmonyMethod(typeof(ApotheonPlugin), nameof(PrefixReplaceClientStart)));
                instance.HarmonyInstance.Patch(networkType.Method("ClientUpdate"),
                                               transpiler: new HarmonyMethod(typeof(ApotheonPlugin), nameof(TranspileClientUpdate)));
            }

            instance.Log.LogInfo($"Patched methods.");
            loaded = true;
        }
        catch (Exception ex)
        {
            instance.Log.LogError(ex);
        }
    }

    private static void PatchPeerDiscovery(object networkInstance)
    {
        if (networkType.GetField("NextClientIntroductionRequest", networkInstance) is not > 3.0d)
            return;

        var client = networkType.GetField("client", networkInstance);

        if (client == null)
            return;

        var ip = networkType.GetField("IPAddress", networkInstance);
        var port = networkType.GetField("Port", networkInstance);

        netClientType.Invoke("DiscoverKnownPeer", new[]
        {
            typeof(string), typeof(int)
        }, client, new[]
        {
            ip, port
        });
    }

    private static IEnumerable<CodeInstruction> TranspileClientUpdate(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var nextClientIntroField = networkType.Field("NextClientIntroductionRequest");

        foreach (var code in codes)
        {
            yield return code;

            // Detect:
            // stfld NextClientIntroductionRequest
            if (code.opcode == OpCodes.Stfld && Equals(code.operand, nextClientIntroField))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ApotheonPlugin), nameof(PatchPeerDiscovery)));
            }
        }
    }

    /// <summary>
    /// Replace ApotheonArena ClientStart with the one from the main game
    /// </summary>
    private static bool PrefixReplaceClientStart(ref object __instance, ref object host)
    {
        try
        {
            if (hostInfoType.GetField("ExternalIP", host) is IPEndPoint externalIp)
            {
                networkType.SetField("IPAddress", __instance, externalIp.Address.ToString());
                networkType.SetField("Port", __instance, externalIp.Port);
            }

            networkType.SetField("HostId", __instance, hostInfoType.GetField("Id", host));

            var config = Activator.CreateInstance(netPeerConfigurationType.Type, "Apotheon");
            // DiscoveryResponse
            netPeerConfigurationType.Invoke("EnableMessageType", config, new object[]
            {
                64
            });
            // NatIntroductionSuccess
            netPeerConfigurationType.Invoke("EnableMessageType", config, new object[]
            {
                2048
            });
            netPeerConfigurationType.SetProperty("EnableUPnP", config, true);

            networkType.SetField("config", __instance, config);
            var client = Activator.CreateInstance(netClientType.Type, config);
            networkType.SetField("client", __instance, client);

            netClientType.Invoke("Start", client, Array.Empty<object>());

            var upnp = netClientType.GetProperty("UPnP", client);
            netUPnPType.Invoke("ForwardPort", upnp, new object[]
            {
                14242, "Apotheon Arena Game"
            });

            // skip original
            return false;
        }
        catch (Exception ex)
        {
            instance.Log.LogError(ex);
            return true;
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
                "# For hosting your own master server see https://github.com/TheXankriegor/ApotheonMultiplayerPatch", "#", $"{DefaultCustomMasterServer}"
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
