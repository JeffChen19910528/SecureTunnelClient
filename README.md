# SecureTunnel Connect

**English** | [繁體中文](README.zh-TW.md)

Windows client for the SecureTunnel private-resource VPN access platform. Lets a user import a Gateway-issued WireGuard configuration, store it securely, and connect to a SecureTunnel Gateway.

> **Status: verified end to end.** A Windows client running the built-in WireGuard engine connected to a real SecureTunnel Gateway (WireGuard + nftables on Linux): handshake completed, the authorized resource was reachable (HTTP 200), an unauthorized resource was blocked, and after Disconnect access was cut off and the virtual adapter removed. This was repeated with the Agent installed as the `SecureTunnelAgent` Windows Service via `deploy\install-service.ps1` (running from `C:\Program Files\SecureTunnel\Agent`). Not yet exercised: the WPF window driven by hand and the WiX MSI.

## Built-in WireGuard engine (no separate install)

The Agent now bundles `wireguard.dll` (wireguard-nt 0.10.1, amd64, see `src/SecureTunnel.Client.Agent/native/win-x64/WIREGUARD-NT-LICENSE.txt`) and drives it in-process, so **WireGuard for Windows does not need to be installed**. The virtual network adapter driver is inside that DLL; the Agent (a Windows Service running elevated) creates the adapter on Connect and removes it on Disconnect or if the Agent exits. IP address, routes and DNS are applied with the OS-provided `netsh.exe`.

- Default engine: embedded. To use an installed WireGuard for Windows instead, set `Tunnel__Engine=WireGuardExe` for the service.
- Limits: 64-bit Windows only; `AllowedIPs` of `0.0.0.0/0` / `::/0` (full tunnel) are rejected for now - list the protected networks instead.
- Verified: struct layout and status mapping by unit tests, and the DLL loads and resolves on this machine. **Not yet verified end to end against a Gateway**, because that needs an elevated session (adapter creation returns Win32 error 5 without it).

## Usage

**What you need**: 64-bit Windows 10/11, the .NET 10 runtime, and a WireGuard client configuration issued by your Gateway (`.conf`). You do **not** need to install WireGuard for Windows.

1. **Start the Agent (administrator, once per boot).** The Agent creates the virtual network adapter, so it must be elevated.
   - Installed as a service: `.\deploy\package.ps1`, then in an elevated PowerShell `.\deploy\install-service.ps1 -ExecutablePath "<path>\SecureTunnel.Client.Agent.exe"` and `Start-Service SecureTunnelAgent`.
   - Just trying it out: in an elevated PowerShell run `SecureTunnel.Client.Agent.exe` and keep the window open.
2. **Run SecureTunnel Connect** (normal user, no elevation). Import the `.conf` file; the summary (gateway, address, allowed networks) is shown. The private key is stored encrypted (DPAPI) by the Agent and never shown again.
3. **Connect.** The state goes Connecting -> Awaiting handshake -> **Connected**. "Connected" appears only after a real WireGuard handshake.
4. **Use the protected resources** (for example `http://10.20.0.5:8443/`). Only networks listed in `AllowedIPs` go through the tunnel; the Gateway still enforces which resources you may reach.
5. **Disconnect** when done. This removes the virtual adapter and its routes. Closing the app does not disconnect; stopping the Agent does.

Configuration rules: `AllowedIPs` must list the protected networks (a full tunnel `0.0.0.0/0` is rejected); `Endpoint` must be reachable over UDP from this PC.

| Message | Meaning / fix |
|---|---|
| "service is not running" | Start the Agent (step 1). |
| Access denied | Use an Agent built after the Named Pipe ACL fix (older builds blocked every user on the pipe). |
| "built-in WireGuard engine is missing" | `wireguard.dll` must sit next to the Agent exe; 64-bit Windows only. |
| "Administrator permission is required" | The Agent is not elevated. |
| Stays at "Awaiting handshake" | Wrong endpoint/port, UDP blocked, or the Gateway does not know your public key. |

## What problem this solves

SecureTunnel's product principle: *only users who connect through a SecureTunnel Gateway and possess an authorized Peer configuration should be able to access protected resources allowed by Gateway policies.* The Gateway server already enforces this on the network side (WireGuard + nftables default-deny). SecureTunnel Connect is the piece that lets a non-technical Windows user actually hold and use an authorized Peer configuration, without needing to understand WireGuard, DPAPI, or run anything as administrator just to see the app.

## Roles

| Term | Meaning |
|---|---|
| Gateway | The SecureTunnel Gateway Server (separate repository) that terminates WireGuard tunnels and enforces access policy. |
| Client / Peer | A single authorized WireGuard configuration issued by a Gateway, imported into this app. |
| SecureTunnel Connect (this repo) | The unelevated WPF app a user runs day-to-day. |
| Privileged boundary (`SecureTunnel.Client.Agent`) | The `SecureTunnelAgent` Worker Service, reached only via a Named Pipe, through which the UI performs Connect/Disconnect/Status/ValidateConfiguration - see `docs/windows-service-boundary.md` and `docs/ipc-protocol.md`. |

## Architecture at a glance

```
SecureTunnel.Client.Core            domain models, interfaces, state machine, IPC wire types (no I/O)
        ^
SecureTunnel.Client.Infrastructure  DPAPI storage, WireGuard connection engine, config parser, Named Pipe client proxy
        ^
SecureTunnel.Client.Agent           Worker Service host ("SecureTunnelAgent"): privileged operation contract + Named Pipe server
        ^
SecureTunnel.Connect                WPF UI (unelevated): import, connect/disconnect, diagnostics, system tray
```

Full diagram and rationale: `docs/architecture.md`.

## Current development status

- [x] Solution/project scaffolding matching the layout above, shared versioning (`Directory.Build.props`, currently `0.6.0`)
- [x] Domain models, validation, and diagnostics types (`SecureTunnel.Client.Core`)
- [x] Client state machine with `Validating`/`InterfaceActive`/`AwaitingHandshake`/`ServiceUnavailable` states - `Connected` reachable only after a verified handshake, hardened against repeated-Connect/repeated-Disconnect conflicts (Phase C5)
- [x] Complete WireGuard `[Interface]`+`[Peer]` configuration parser and deterministic generator (mandatory peer public key, mandatory validated endpoint, AllowedIPs never defaulted)
- [x] DPAPI-encrypted (`LocalMachine` scope), atomic, fail-closed configuration storage - real same-identity cross-scope compatibility verified (Phase C4)
- [x] WireGuard connection engine: real interface/handshake status parsing (`WireGuardDumpParser`), per-operation timeout with process-tree kill (real-process-verified), idempotent tunnel-scoped disconnect, stdout/stderr redaction, temp-file cleanup verified on the timeout path
- [x] Real .NET 10 Worker Service host (`SecureTunnel.Client.Agent`, service name `SecureTunnelAgent`)
- [x] Hardened Named Pipe IPC transport: fixed pipe name, framed JSON, size limits, timeouts, closed operation allow-list, `BUILTIN\Users`-scoped ACL denying Everyone/Anonymous/Network - see `docs/ipc-protocol.md`
- [x] WPF: configuration import (file picker, validation display, configuration summary), Connect/Disconnect/Refresh, diagnostics panel, system tray (minimize/restore/exit, never touches the Agent connection)
- [x] A 14-scenario named WireGuard test double (`FakeWireGuardClientService`) for standalone client verification without a real Gateway or WireGuard install (Phase C5)
- [x] Windows Service install/uninstall PowerShell scripts - idempotent version/path detection, restricted-ACL data-directory provisioning, explicit recovery config, opt-in data retention on uninstall (Phase C6) - written and syntax-checked, **not executed**
- [x] Repeatable local packaging (`deploy\package.ps1`) - **actually run** this phase, producing real versioned/checksummed artifacts and catching a real embedded-developer-path defect (fixed) - see `docs/c6-packaging-report.md`
- [x] 168/168 automated tests passing, including real in-process Named Pipe round trips, a real-child-process timeout/kill test, and real DPAPI round trips - re-confirmed clean across 3 consecutive full-suite runs after fixing a real load-dependent test flakiness defect in Phase C7 (`docs/testing-guide.md`)
- [x] Real Windows acceptance environment tooling (Phase C7-ENV): a non-destructive readiness probe (`deploy/check-c7-environment.ps1`, real-executed - 13/16 READY, 3 NOT READY on this machine), a sanitized read-only evidence harness (`deploy/collect-c7-evidence.ps1`), a manual elevated-setup guide, and 16/16 real Pester tests covering the new tooling (which themselves found and fixed two real PowerShell defects, including a deny-only-Administrators-token detection bug) - see `docs/c7-environment-readiness.md`. **Does not replace real C7 acceptance**, which remains Blocked.
- [ ] Actually-installed Windows Service - **Blocked** (no elevated access in this environment; see `docs/windows-service-installation.md`)
- [ ] Cross-identity Named Pipe access-denied test - **Blocked** (no second Windows identity available)
- [x] Real WireGuard connectivity - verified against a real Gateway with the built-in engine (see Usage); a Named Pipe ACL bug that locked out every user was fixed along the way
- [ ] Tray icon / file dialog / clipboard UI automation - **Blocked** (requires an interactive Windows session)
- [ ] Production installer (WiX MSI) - **Deferred**, evaluated and recommended, not built - see `docs/installer-technology-assessment.md`

## Project structure

```
SecureTunnelClient/
├── SecureTunnelClient.slnx
├── Directory.Build.props                   shared version + build-path hygiene (PathMap)
├── deploy/
│   ├── package.ps1                         repeatable local packaging - actually run, see docs/c6-packaging-report.md
│   ├── install-service.ps1                 not executed - see docs/windows-service-installation.md
│   ├── uninstall-service.ps1
│   ├── C7EnvironmentChecks.psm1            readiness-check helpers - real-executed, 16 Pester tests
│   ├── check-c7-environment.ps1            non-destructive readiness probe - real-executed
│   ├── collect-c7-evidence.ps1             read-only, sanitized evidence-collection harness for a future C7 rerun
│   └── tests/
│       └── C7EnvironmentChecks.Tests.ps1   16/16 passing (Pester 3.4.0)
├── src/
│   ├── SecureTunnel.Connect/               WPF application (import, connect, diagnostics, tray)
│   ├── SecureTunnel.Client.Core/           domain models, interfaces, IPC wire protocol types
│   ├── SecureTunnel.Client.Agent/          Worker Service host: privileged operation contract + Named Pipe server
│   └── SecureTunnel.Client.Infrastructure/ Windows/WireGuard integration abstractions + Named Pipe client proxy
├── tests/
│   ├── SecureTunnel.Client.Core.Tests/
│   ├── SecureTunnel.Client.Agent.Tests/
│   ├── SecureTunnel.Client.Infrastructure.Tests/
│   ├── SecureTunnel.Client.Connect.Tests/  WPF ViewModel tests
│   └── SecureTunnel.Client.TestSupport/    shared fakes/test doubles
└── docs/
    ├── architecture.md
    ├── security-model.md
    ├── configuration-format.md
    ├── windows-service-boundary.md
    ├── windows-service-installation.md
    ├── installer-technology-assessment.md
    ├── deployment-guide.md
    ├── ipc-protocol.md
    ├── wireguard-connection-lifecycle.md
    ├── diagnostics.md
    ├── testing-guide.md
    ├── c4-acceptance-report.md
    ├── c5-standalone-client-acceptance.md
    ├── c6-packaging-report.md
    ├── c7-acceptance-report.md
    ├── c7-environment-readiness.md
    ├── c7-elevated-setup-guide.md
    ├── c7-evidence-collection.md
    ├── c7-cleanup-guide.md
    ├── c7-env-report.md
    ├── final-client-closeout-report.md
    └── roadmap.md
```

## Building, testing, and packaging

Requires the .NET 10 SDK on Windows.

```
dotnet build SecureTunnelClient.slnx
dotnet test SecureTunnelClient.slnx
.\deploy\package.ps1
```

See `docs/testing-guide.md` for the actual test results and `docs/c6-packaging-report.md` for the actual packaging results against this codebase.
