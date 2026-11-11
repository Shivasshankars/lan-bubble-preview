# LAN Bubble (Windows)

A small Windows desktop app for sending short, styled notification bubbles to other computers on the same local Ethernet LAN. Wi-Fi is not required.

## Requirements

- Windows 10/11
- .NET 8 SDK (or Visual Studio 2022 with the .NET desktop development workload)
- The receiving computers must run the app and be reachable on the same broadcast-enabled subnet/VLAN

## Run

From this directory, in PowerShell:

```powershell
dotnet run --project LanBubble/LanBubble.csproj
```

Or open `LanBubble.sln` in Visual Studio and press **F5**.

## Use

1. Run the app on each Windows computer that should receive bubbles.
2. Select a tone, enter a message, choose a screen corner and size, then click **Send bubble**.
3. The sender and other listening computers show a temporary desktop notification and add it to the app's message history.

The app sends UDP broadcast packets on port **50000**. On first run, allow the app through Windows Defender Firewall for the trusted **Private** or **Domain** network profile. Do not enable it for Public networks unless you understand the risk. Broadcasts generally do not pass through routers or VLAN boundaries; network policies may block them. This is an unauthenticated LAN utility, so only use it on a trusted network. Messages are limited to 500 characters and are not encrypted.

## Publish a self-contained Windows build

On a Windows computer with the .NET 8 SDK:

```powershell
dotnet publish LanBubble/LanBubble.csproj -c Release -r win-x64 --self-contained true
```

The app will be under `LanBubble/bin/Release/net8.0-windows/win-x64/publish/`.
