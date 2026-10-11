# SecureTunnel Connect

**English** | [繁體中文](README.zh-TW.md)

SecureTunnel Connect is a Windows VPN client for a [SecureTunnel Gateway](https://github.com/JeffChen19910528/SecureTunnelGateway). Import the WireGuard configuration your Gateway issued, click **Connect**, and reach the private resources you are allowed to use. Everything it needs is built in: you do **not** have to install WireGuard for Windows or any other VPN software.

- One-click connect / disconnect with a live status ("Connected" only after a real handshake)
- Built-in WireGuard engine (virtual adapter included)
- Imported keys are stored encrypted (Windows DPAPI) and are never shown again
- Only the networks your Gateway allows go through the tunnel
- Runs in the system tray; English and Traditional Chinese interface

## Requirements

- Windows 10 / 11, 64-bit
- A WireGuard client configuration (`.conf`) issued by your Gateway administrator
- Administrator rights **once**, to install (the background service creates the virtual network adapter)
- Nothing else: the installer already contains the .NET runtime and the WireGuard engine

## Install

**With the installer (recommended).** Build it as described under [Build from source](#build-from-source), then run `SecureTunnelConnect-Setup-<version>-win-x64.exe` (it asks for administrator rights). It installs to `C:\Program Files\SecureTunnel`:

- the **SecureTunnel Agent** Windows service (starts automatically, runs in the background), and
- the **SecureTunnel Connect** app, with a Start menu shortcut and an entry in *Settings > Apps*.

Run it again with a newer version to upgrade; imported configurations are kept. Silent install: `SecureTunnelConnect-Setup-<version>-win-x64.exe /silent`.

**Without the installer.** After running `deploy\package.ps1`, copy `artifacts\<version>\Agent` to `C:\Program Files\SecureTunnel\Agent` and, from an administrator PowerShell:

```powershell
.\deploy\install-service.ps1 -ExecutablePath "C:\Program Files\SecureTunnel\Agent\SecureTunnel.Client.Agent.exe"
Start-Service SecureTunnelAgent
```

Then run `SecureTunnel.Connect.exe` from `artifacts\<version>\Connect`.

## Use

1. Open **SecureTunnel Connect** (normal user, no administrator rights needed).
2. Click **Import Configuration...** and choose the `.conf` file from your Gateway administrator. The Gateway address, your tunnel address and the allowed networks are shown. Importing the same file again simply replaces it.
3. Click **Connect**. The status goes *Awaiting handshake* and then **Connected**.
4. Use the protected resources as usual (browser, database client, ...). Which resources you may reach is decided by the Gateway.
5. Click **Disconnect** when you are done. Closing the window only minimizes it to the tray; the VPN stays connected until you disconnect.

The **Language** button switches between English and Traditional Chinese. **Refresh Diagnostics** and **Copy to Clipboard** produce a short, secret-free report you can send to your administrator.

## Configuration file

The file is a standard WireGuard client configuration:

```ini
[Interface]
PrivateKey = <your private key>
Address = 10.250.0.10/32
DNS = 10.250.0.1            # optional
MTU = 1380                  # optional

[Peer]
PublicKey = <Gateway public key>
AllowedIPs = 10.20.0.0/24, 10.250.0.0/24
Endpoint = gateway.example.com:51820
PersistentKeepalive = 25    # optional
```

- `PublicKey`, `Endpoint` and `AllowedIPs` are required; `AllowedIPs` is never filled in for you.
- `AllowedIPs` must list the protected networks. A full tunnel (`0.0.0.0/0` or `::/0`) is not supported and is rejected.
- `Endpoint` must be reachable from your PC over UDP.

## Troubleshooting

| What you see | What to do |
|---|---|
| "The SecureTunnel Connect service is not running" | Start it: `Start-Service SecureTunnelAgent` (administrator). |
| Stays at **Awaiting handshake** | Check the `Endpoint` address and port, that UDP is not blocked by a firewall, and that the Gateway administrator registered your public key. |
| "Administrator permission is required" | The service is not running as the system service; reinstall it with the installer. |
| "The built-in WireGuard engine is missing" | `wireguard.dll` must be in the same folder as `SecureTunnel.Client.Agent.exe`; only 64-bit Windows is supported. |
| Connected, but a resource is unreachable | The Gateway only allows the resources your administrator granted. Ask them to check your access. |
| Access to the service was denied | Reinstall the current version. |

## Uninstall

Remove **SecureTunnel Connect** from *Settings > Apps*, or run `"C:\Program Files\SecureTunnel\Setup\SecureTunnelSetup.exe" /uninstall` (add `/removedata` to also delete the imported configurations, `/silent` for no prompts). Imported configurations are kept in `%ProgramData%\SecureTunnel\Agent` so a reinstall does not need a new import; delete that folder to remove them. Without the installer: `.\deploy\uninstall-service.ps1` (add `-RemoveData` to delete the configurations).

## Security

See [docs/security.md](docs/security.md).

## Build from source

Requires Windows and the .NET 10 SDK. No other tool (no installer toolkit) is needed.

```powershell
dotnet build SecureTunnelClient.slnx
dotnet test  SecureTunnelClient.slnx
.\deploy\package.ps1       # artifacts\<version>\{Agent,Connect}, checksums and zips (self-contained)
.\deploy\build-setup.ps1   # artifacts\<version>\SecureTunnelConnect-Setup-<version>-win-x64.exe
```

The tests include UI automation (`SecureTunnel.Client.UiTests`) that opens the app and needs an interactive desktop. A few pipe tests are skipped while the SecureTunnel Agent service is running on the same PC.

The bundled `wireguard.dll` is [wireguard-nt](https://www.wireguard.com/) (WireGuard LLC); its license is in `src/SecureTunnel.Client.Agent/native/win-x64/WIREGUARD-NT-LICENSE.txt`.
