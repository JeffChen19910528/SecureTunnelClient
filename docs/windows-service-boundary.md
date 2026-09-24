# Windows Service Boundary

## Status

- **Implemented & Tested & Passed:** the request/response contract types (now including a `ConfigurationSummary` on successful validation), the closed dispatcher, the foundation handler (`ClientAgentServiceFoundation`), the .NET 10 Worker Service host (`Program.cs`, `ServiceName="SecureTunnelAgent"`, `UseWindowsService()`), and the Named Pipe transport (`NamedPipeHostedService`, `PipeSessionHandler`, hardened `PipeSecurityFactory`) - all in `SecureTunnel.Client.Agent`. Covered by `PrivilegedOperationDispatcherTests`, `ClientAgentServiceFoundationTests`, `PipeSessionHandlerTests`, `PipeSecurityFactoryTests`, and `NamedPipeIntegrationTests` (real in-process pipe round trips).
- **Blocked:** actually installing this as a Windows Service (`New-Service`, LocalSystem identity) has not been done in this phase - see `windows-service-installation.md`. The code is *capable* of `UseWindowsService()` hosting but has only been run as an ordinary process during tests.
- **Accepted Risk:** the pipe's access control grants `BUILTIN\Users` broadly rather than a specific interactive-user SID, unverified against a real second identity at runtime - see `ipc-protocol.md`.

## Service Identity Rationale

The Agent is designed to run as **LocalSystem**. This was not chosen for convenience - it was evaluated against `NetworkService`, `LocalService`, and a dedicated restricted service account, and rejected those because:

- `wireguard.exe /installtunnelservice`/`/uninstalltunnelservice`/`/dumptunnelservice` themselves require rights roughly equivalent to `SC_MANAGER_CREATE_SERVICE` on the Service Control Manager (they create/manage a *second*, WireGuard-owned service per tunnel) and write access to `%ProgramData%\WireGuard`. Neither `NetworkService` nor `LocalService` has these rights by default.
- A dedicated restricted service account would need to be granted essentially SCM-admin-equivalent rights to make those WireGuard CLI calls succeed at all - at which point it provides no real privilege reduction over LocalSystem, only additional operational complexity (account provisioning, credential rotation) with no corresponding security benefit for this specific workload.
- The Agent's own attack surface is already minimized by the four-operation allow-list (see "The contract" below) and the DPAPI-`LocalMachine`-scoped storage (see `security-model.md`) - the compensating control for running as LocalSystem is the **Named Pipe ACL**, not the process identity, which is why that ACL is the thing hardened and tested in this phase (see `ipc-protocol.md`).

This is a documented design decision, not a claim that LocalSystem is risk-free - it is the least-bad option given what the WireGuard CLI itself requires.

## Why a separate privileged boundary exists

SecureTunnel Connect's WPF process must be runnable by a non-technical user without an administrator prompt just to see the UI. But connecting/disconnecting a WireGuard tunnel and reading DPAPI-protected private keys are privileged operations on Windows. Rather than either (a) requiring the whole UI to run elevated, or (b) letting the UI process touch WireGuard/DPAPI directly, this boundary confines all privileged access to `SecureTunnel.Client.Agent`, which is now a real .NET 10 Worker Service process (`Program.cs` calls `Host.CreateApplicationBuilder().Services.AddWindowsService(...)`) that the UI talks to over the Named Pipe transport documented in `ipc-protocol.md`, using the narrow, typed contract below.

`src/SecureTunnel.Connect/app.manifest` sets `requestedExecutionLevel="asInvoker"` - the UI process never requests elevation.

## The contract

```csharp
public abstract record PrivilegedRequest(string ClientId);
public sealed record ConnectRequest(string ClientId) : PrivilegedRequest(ClientId);
public sealed record DisconnectRequest(string ClientId) : PrivilegedRequest(ClientId);
public sealed record StatusRequest(string ClientId) : PrivilegedRequest(ClientId);
public sealed record ValidateConfigurationRequest(string ClientId, string RawConfigText) : PrivilegedRequest(ClientId);

public sealed record PrivilegedResponse(
    bool Success,
    WireGuardOutcome Outcome,
    string? ErrorCode,
    string? UserSafeMessage,
    ClientState? ObservedState,
    ConfigurationSummary? Configuration = null);

public interface IPrivilegedOperationHandler
{
    Task<PrivilegedResponse> HandleConnectAsync(ConnectRequest request, CancellationToken cancellationToken);
    Task<PrivilegedResponse> HandleDisconnectAsync(DisconnectRequest request, CancellationToken cancellationToken);
    Task<PrivilegedResponse> HandleStatusAsync(StatusRequest request, CancellationToken cancellationToken);
    Task<PrivilegedResponse> HandleValidateConfigurationAsync(ValidateConfigurationRequest request, CancellationToken cancellationToken);
}
```

`PrivilegedRequest` is `abstract` with exactly four `sealed` subtypes. `PrivilegedOperationDispatcher.DispatchAsync` is a closed `switch` expression over those four types; any other `PrivilegedRequest` subtype (there should never be one outside this file) throws `NotSupportedException` rather than being executed. This is the enforcement point verified by `Dispatcher_OnlyExposesAllowListedOperations` and `Dispatcher_HasNoGenericExecuteMethod`.

## What is deliberately NOT exposed

- **Arbitrary process execution.** There is no `Execute(string command)`, `RunCommand(...)`, or any method accepting a free-form command string.
- **Arbitrary file path access.** Requests carry a `ClientId`, not a file path; `ClientAgentServiceFoundation` is the only code that resolves a `ClientId` to a storage location, via `IClientConfigurationStore`.
- **Raw private key retrieval by the UI.** `PrivilegedResponse` never carries a private key or `SensitiveString`. The UI can request Connect/Disconnect/Status/ValidateConfiguration - it can never ask the privileged side to hand back key material.
- **Firewall rule mutation.** No such operation exists in this contract (matches the "no Windows firewall rule changes this phase" security requirement).

## Transport

The contract above now crosses a real process boundary via a Named Pipe (full protocol details, framing, timeouts, and access control in `ipc-protocol.md`). `ClientAgentServiceFoundation` and `PrivilegedOperationDispatcher` remain plain, dependency-injectable classes with no coupling to the transport itself - `PipeSessionHandler` is a thin adapter that deserializes a `PipeRequestEnvelope`, maps it to the matching `PrivilegedRequest` subtype, calls `PrivilegedOperationDispatcher.DispatchAsync`, and serializes the `PrivilegedResponse` back out as a `PipeResponseEnvelope`. The WPF UI reaches this boundary through `SecureTunnel.Client.Infrastructure.Ipc.NamedPipePrivilegedClientProxy`, which implements `SecureTunnel.Client.Core.Client.IPrivilegedClientService` - the UI's `MainViewModel` depends only on that interface, never on anything in `SecureTunnel.Client.Agent`.

**What remains deferred:** actually installing `SecureTunnel.Client.Agent` as a registered Windows Service (`sc create`/`New-Service`, choosing and provisioning a service account, startup type, recovery behavior) has not been done in this phase. The host code is written to support it (`UseWindowsService()`), but every automated test in this repository runs the Worker Service's hosted-service logic as an ordinary process, not as an installed service - that gap is intentional and documented, not silently assumed away.
