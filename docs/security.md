# Security overview

## How it is split

| Part | Runs as | Does |
|---|---|---|
| **SecureTunnel Connect** (app) | the logged-in user, no elevation | Import, connect/disconnect buttons, status and diagnostics. Never sees a private key after import. |
| **SecureTunnel Agent** (service) | Windows service, LocalSystem | Stores configurations, creates the WireGuard adapter, sets addresses/routes/DNS, reports status. |

The app talks to the Agent only through a local **Named Pipe**. The Agent accepts a small, fixed set of operations (validate/import, connect, disconnect, status); nothing else can be requested.

## Named Pipe access

- Allowed: `BUILTIN\Users` (read/write), `SYSTEM` and `BUILTIN\Administrators` (full control).
- Denied: `ANONYMOUS LOGON` and `NT AUTHORITY\NETWORK`, so remote and anonymous callers cannot connect.
- Messages are size-limited and time-limited; the pipe name is fixed and cannot be chosen by a caller.
- Any local user account on the PC can use the pipe. On a shared PC, anyone who can log in can connect with the configuration that is stored.

## Secrets

- The private key (and preshared key, if any) is stored encrypted with Windows DPAPI (machine scope) in `%ProgramData%\SecureTunnel\Agent\configs`. The folder is restricted to SYSTEM and Administrators, writes are atomic, and a damaged file is reported as an error instead of being overwritten.
- Keys are never written to logs, diagnostics, the clipboard report, or the pipe responses. Error messages are scrubbed of key material.
- The app shows only the Gateway address, your tunnel address and the allowed networks.

## Tunnel behavior

- The status "Connected" is reported only after a WireGuard handshake was observed (and it falls back if the last handshake is stale).
- The virtual adapter lives only while the Agent holds it: disconnecting, stopping the service or a crash removes it.
- Only the networks in `AllowedIPs` are routed through the tunnel. What you may reach inside them is enforced by the Gateway firewall, not by this client.
- Full-tunnel configurations (`0.0.0.0/0`, `::/0`) are rejected.

## Bundled component

The service uses `wireguard.dll` from wireguard-nt (WireGuard LLC), loaded from its own folder and used only through its public API. Verify the file you ship against the official download.
