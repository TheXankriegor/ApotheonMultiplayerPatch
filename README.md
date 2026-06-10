# Apotheon Multiplayer Patch

A BepInEx mod for Apotheon & Apotheon Arena that **fixes multiplayer** by replacing the original master server with a self-hosted alternative and by patching direct IP join.

## What it does

- **Fixes multiplayer**: The official master server is no longer available. This mod redirects the game to a functional master server, restoring server browser functionality.
- **Enables direct join**: Allows direct IP-based connections to game servers without relying on the master server. As long as the host has port **14242** forwarded, players can join via IP.

## Installation

1. Download the [BepInEx-NET.Framework-net40](https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.2/BepInEx-NET.Framework-net40-win-x86-6.0.0-pre.2.zip) release from [BepInEx v6.0.0-pre.2](https://github.com/BepInEx/BepInEx/releases/tag/v6.0.0-pre.2) (or any of the following 6.X releases) and extract it into your Apotheon game directory
2. Download the [latest release](https://github.com/TheXankriegor/ApotheonMultiplayerPatch/releases/latest) of this mod and place it in `BepInEx/plugins/`
3. In `BepInEx\config\BepInEx.cfg` set Assembly to `Assembly = Apotheon.exe` (or `ApotheonArena.exe`) and save
4. Launch the game via `BepInEx.NET.Framework.Launcher.exe`
5. The mod should load in the console log:

```
[Info   :   BepInEx] 1 plugin to load
[Info   :   BepInEx] Loading [Apotheon Multiplayer Patch 1.0.0]
[Info   :Apotheon Multiplayer Patch] Initializing Apotheon Multiplayer Patch v1.0.0
```

The mod will automatically create a `settings.cfg` file in `BepInEx/plugins/`. Edit it to specify your master server address if you are hosting your own.

## Master Server

The master server replaces the defunct official one, handling server listing and NAT introduction for player discovery. Port **14343** is used for communication with the master server; port **14242** is used for all client/game connections.

**To launch:**
```bash
dotnet run --project ApotheonMasterServer
```

Or use Docker:
```bash
docker build -t apotheon-master . && docker run -p 14343:14343 apotheon-master
```
