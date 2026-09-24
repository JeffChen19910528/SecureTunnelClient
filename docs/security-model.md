# Security Model

## Status

- **Implemented & Tested & Passed:** private-key/preshared-key redaction (`SensitiveString`), DPAPI-encrypted-at-rest storage (now `LocalMachine` scope) with fail-closed corruption handling, atomic writes, argument-list-only process invocation with a per-operation timeout and process-tree kill on cancellation, no-generic-command-execution service boundary, a hardened Named Pipe transport that never carries key material and enforces size limits/timeouts/an operation allow-list, explicit AllowedIPs (never silently defaulted or widened), idempotent tunnel-scoped disconnect, stdout/stderr redaction. All verified by the automated tests listed in `testing-guide.md` (see current pass count there).
- **Not Implemented this phase:** Windows firewall rule changes (explicitly out of scope), full-tunnel routing, real Windows Service installation, a production-grade per-user pipe ACL.
- **Phase C6 addition:** the `%ProgramData%\SecureTunnel\Agent\configs` data directory (where DPAPI-protected configurations live) is now provisioned with a restricted ACL (`NT AUTHORITY\SYSTEM` + `BUILTIN\Administrators` only, inheritance from `%ProgramData%`'s default `BUILTIN\Users`-readable ACL explicitly removed) by `deploy/install-service.ps1`. Also this phase: a real packaging run (`deploy/package.ps1`) found and fixed a genuine information-disclosure defect - published binaries embedded the developer's local absolute build path (username, folder structure) in their PDB debug-directory metadata; fixed via explicit Roslyn `PathMap` in `Directory.Build.props`. See `docs/c6-packaging-report.md`.
- **Phase C7 addition (real, not simulated):** the ACL-provisioning mechanism above was independently exercised for real (against a disposable probe directory, not the product's real data path) and confirmed genuinely restrictive - the same non-elevated session that created the probe directory and applied the ACL could not subsequently delete it. A real `New-Service` call was also attempted unelevated and failed with `Access is denied`, confirming Windows Service registration genuinely requires elevation in this environment (not merely assumed). See `docs/c7-acceptance-report.md`. Real cross-identity Named Pipe ACL denial, real installed-service runtime, and real WireGuard connectivity remain unverified - the user was asked and declined to provide the required elevated/WireGuard/second-identity environment this phase.
- **Phase C7-ENV addition:** built a repeatable, non-destructive readiness probe (`deploy/check-c7-environment.ps1`) and a sanitized evidence-collection harness (`deploy/collect-c7-evidence.ps1`), both real-executed this phase. Their own development surfaced a security-relevant correctness defect worth noting here specifically: the first implementation of "is this account an Administrators-group member" used `[WindowsIdentity]::GetCurrent().Groups`, which does **not** reliably report deny-only group membership - meaning a naive elevation/membership check of this shape could under-report exactly the "Administrators member, but running unelevated" condition this project's security model depends on distinguishing correctly (see `windows-service-boundary.md`'s Service Identity Rationale and every acceptance report since C4). Fixed by querying `Get-LocalGroupMember` instead, which reflects real account membership independent of token filtering. This was a tooling defect, not a defect in the shipped product (`SecureTunnel.Client.Agent`/`SecureTunnel.Connect` never perform this specific check), but is documented here because the underlying pitfall - conflating token-group enumeration with actual group membership - is exactly the kind of mistake this project's threat model exists to avoid.
- **Accepted Risk:** the Named Pipe access control grants `BUILTIN\Users` (broader than a single interactive user, narrower than Everyone) and has not been validated against a real second Windows identity - see `ipc-protocol.md`. The temp tunnel file's ACL restriction has a brief creation-to-restriction TOCTOU window - see `wireguard-connection-lifecycle.md`.
- **Final closeout audit addition:** a repository-wide security scan (private-key/secret grep, generic-command-execution surface check, WPF elevation manifest check, IPC envelope key-material check) re-confirmed every item above with no new finding. See `docs/final-client-closeout-report.md` Section E for the full 20-item checklist and classification (code-inspection vs. real-OS-execution vs. test-verified).
- **Known Limitations:** see bottom of this document.

## Threat this client addresses

Only a user who (a) runs SecureTunnel Connect and (b) possesses a Gateway-issued WireGuard client configuration should be able to reach protected resources behind a SecureTunnel Gateway. This client is not itself a trust boundary for anonymity or general-purpose VPN use - see "What this client does not do" below.

## Private key protection

- **In memory:** private keys are never held as `string`. `SecureTunnel.Client.Core.Security.SensitiveString` wraps the value; `ToString()` unconditionally returns `"***REDACTED***"`, and the raw value is reachable only through the explicit `Reveal()` method, which is doc-commented as never to be logged.
- **At rest:** `DpapiClientConfigurationStore` encrypts the entire configuration (including the private key) with Windows DPAPI (`ProtectedData`, `LocalMachine` scope as of Phase C3 - see below) before it ever touches disk. `SaveAsync_EncryptsPrivateKeyAtRest` verifies the on-disk bytes do not contain the plaintext key.
- **On disk, transiently:** `WireGuardClientService.ConnectAsync` writes the private key to a short-lived temp `.conf` file (required by the WireGuard CLI) and deletes it in a `finally` block immediately after the external process exits.
- **In command-line arguments:** never. `WireGuardClientService` passes only a file *path* to the external process via `ProcessStartInfo.ArgumentList` - the key value itself is never an argv token. Verified by `ConnectAsync_NeverPassesPrivateKeyAsArgument`.
- **In logs / exceptions:** `DiagnosticResultFactory.FromException` strips any 42-44 character base64-like token (the shape of a WireGuard key) from exception messages before they become `TechnicalDetails`. Storage error messages (`StoreResult`/`LoadResult`) are hand-authored user-safe strings, never raw exception text.
- **In the Named Pipe protocol:** `PipeRequestEnvelope`/`PipeResponseEnvelope` (see `ipc-protocol.md`) have no field for a private key or preshared key. `ValidateConfiguration` requests carry raw imported config text, but the server wraps any discovered key in `SensitiveString` the instant it's parsed and never echoes it back. Verified by `NamedPipeIntegrationTests.Response_NeverContainsPrivateKeyOrPresharedKey`.
- **Preshared key:** treated identically to the private key - `ClientConfiguration.PresharedKey` is a `SensitiveString`, encrypted at rest alongside the private key via the same DPAPI call, and only ever written to the same short-lived temp tunnel file (`TunnelFileTextBuilder`), never logged or placed in a pipe response.

## Fail-closed storage

`DpapiClientConfigurationStore.LoadAsync` never returns a partially-valid or best-effort result. On any decryption failure (`CryptographicException`) or deserialization failure (`JsonException`, null result), it returns `LoadResult.Success = false` and leaves the on-disk file untouched - it never overwrites a corrupted file with a "fixed" version, and never treats corrupted bytes as an empty/default configuration. Verified by `LoadAsync_CorruptedFile_FailsClosed`.

## Atomic writes

`SaveAsync` writes to a uniquely-named temp file in the same directory and then performs a single `File.Move(..., overwrite: true)`, so a reader never observes a partially-written file. If the underlying `IAtomicFileWriter` throws (disk full, permission denied, etc.), `SaveAsync` reports `StorageErrorCodes.AtomicWriteFailed` and the previously-stored file is left untouched. Verified by `SaveAsync_AtomicWriteFailure_DoesNotCorruptExistingFile`.

## No shell command construction

`WireGuardProcessRunner` uses `ProcessStartInfo.ArgumentList` exclusively; the codebase contains no `ProcessStartInfo.Arguments = ...` string assignment (verified by inspection: `grep -rn "\.Arguments\s*="` across `src/` returns no matches). No code path builds a command string and hands it to a shell.

## No generic privileged command execution

The only way to reach WireGuard/DPAPI code from a less-trusted context is through `SecureTunnel.Client.Agent.Contracts.IPrivilegedOperationHandler`, which exposes exactly four methods - Connect, Disconnect, Status, ValidateConfiguration - and `PrivilegedOperationDispatcher`, whose switch statement rejects any request type outside that fixed set. There is no `Execute(string command)` or reflection/string-keyed dispatch anywhere in `SecureTunnel.Client.Agent`. Verified by `Dispatcher_OnlyExposesAllowListedOperations` and `Dispatcher_HasNoGenericExecuteMethod`. This now extends across the process boundary: `PipeSessionHandler` validates the incoming `Operation` string against the same four-item allow-list before it ever constructs a `PrivilegedRequest`, so a malicious or buggy client cannot reach anything beyond those four operations even from a different process. Verified by `PipeSessionHandlerTests.UnknownOperation_ReturnsInvalidRequest` and `UnsupportedOperation_Rejected`.

## Named Pipe transport hardening

- **Fixed pipe name, not caller-suppliable:** `PipeProtocol.PipeName` is a compile-time constant; neither the server nor the client accept a pipe name parameter.
- **Message size limit:** 64 KiB, enforced by `PipeFraming.ReadFrameAsync` against the length prefix *before* any body buffer is allocated - an attacker cannot force a large allocation just by sending a large declared length. Verified by `PipeSessionHandlerTests.OversizedMessage_RejectedBeforeRead`.
- **No indefinite blocking:** every server session has a 30-second operation budget and every client call has a 35-second overall budget (`PipeProtocol`), both enforced via linked `CancellationTokenSource`s.
- **Malformed/unknown input fails closed as data, not as a crash:** `PipeSessionHandler` converts JSON errors, unknown operations, and size violations into a structured `InvalidRequest` response and keeps the accept loop alive for the next client, rather than throwing out of the connection handler.
- **Access control:** see `ipc-protocol.md` for the `BUILTIN\Users`-scoped `PipeSecurity` (denying `Everyone`/`ANONYMOUS LOGON`/`NETWORK`) and its accepted-risk status.

## Storage path restrictions and DPAPI scope change (Phase C3)

`DpapiClientConfigurationStore` now defaults to `%ProgramData%\SecureTunnel\Agent\configs` and uses `DataProtectionScope.LocalMachine` (was `%LOCALAPPDATA%\SecureTunnel\Connect\configs` / `DataProtectionScope.CurrentUser` through Phase C2). This is a deliberate consequence of the Agent being designed to eventually run as LocalSystem (see `windows-service-boundary.md`'s Service Identity Rationale).

**Corrected in Phase C4 with real-Windows evidence, replacing an earlier assumption:** `LoadAsync_LegacyCurrentUserScopedData_SameIdentity_ActuallyDecryptsSuccessfully` (run on this real Windows 11 machine, not mocked) shows that Windows `CryptUnprotectData` determines which master key to use from metadata embedded in the DPAPI blob itself, not solely from the scope flag an application passes to `Unprotect`. When the **same Windows identity** that encrypted a `CurrentUser`-scoped blob later reads it with a `LocalMachine`-scoped `DpapiClientConfigurationStore`, decryption still succeeds - the scope-flag change alone does not make legacy data unreadable for that identity, and no data loss occurs on a single-user desktop where the same account both created the Phase C1/C2 data and now runs a C3+ build unelevated.

**What remains a genuine, untested risk:** once the Agent actually runs as **LocalSystem** (a different Windows identity than the interactive user), LocalSystem cannot access that interactive user's DPAPI user-profile master key - a `CurrentUser`-scoped blob created by the interactive user would then be unreadable by the installed service. This cross-identity case was **not tested** in Phase C4 (no elevated/installed LocalSystem process was available in this environment - see `windows-service-installation.md`) and remains a real open question for Phase C5's real service installation. No migration code exists either way - re-import remains the documented recovery path if a cross-identity read ever does fail, and is treated as a Deferred Item.

A caller-supplied storage root is only accepted if it resolves under the machine-wide application data directory, the current user's local application data directory, or the system temp directory (the latter exists solely so tests can use an isolated, disposable root); any other path throws `ArgumentException` before any file operation occurs.

## Connectivity is never claimed from a process exit code alone, or from a successful pipe call alone

`ClientStateMachine` only allows a transition into `ClientState.Connected` via the `ConnectionConfirmed` trigger, which callers must only fire after `IWireGuardClientService.GetStatusAsync` corroborates an active tunnel. `WireGuardClientService.ConnectAsync` itself never returns `WireGuardOutcome.Connected` - a successful process exit only yields `WireGuardOutcome.Unknown` with a message stating that status confirmation is still required. This holds all the way across the Named Pipe boundary: `PipeSessionHandler` forwards whatever outcome `ClientAgentServiceFoundation`/`WireGuardClientService` actually produced without upgrading it, and `NamedPipePrivilegedClientProxy` does the same on the way back to the UI - a pipe round trip completing successfully (`Success = true`) is not itself evidence of connectivity; only an `Outcome`/`ObservedState` of `Connected` (which only `GetStatusAsync` can produce) is.

## What this client explicitly does not do

- It does not implement WireGuard's cryptography - it shells out to the official WireGuard tooling.
- It does not provide anonymity; it is a managed-access client for a specific Gateway's protected resources, not a privacy/anonymity VPN.
- It does not bypass Gateway access policies - all access control remains enforced by the Gateway's nftables default-deny model.
- It does not provide unrestricted LAN access.
- It does not automatically turn into a full-tunnel VPN; `AllowedIPs` is required, explicit, and taken as-is from the imported configuration - no code path in this repository ever defaults or widens it to `0.0.0.0/0`.
- It does not modify Windows firewall rules (explicitly out of scope this phase).

## Known Limitations

- No real Windows Service is installed; `SecureTunnel.Client.Agent` is only *capable* of running as one (`UseWindowsService()`). The privileged boundary and the Named Pipe transport are proven via real in-process pipe round trips, not via an actually-elevated, actually-installed service process. See `windows-service-boundary.md` and `windows-service-installation.md`.
- The Named Pipe access control (`PipeSecurityFactory`) grants `BUILTIN\Users` and is unverified against a real second identity at runtime - see `ipc-protocol.md`'s Accepted Risk note.
- Pre-C3 DPAPI-`CurrentUser`-scoped saved configurations ARE readable by this version when accessed by the same Windows identity that created them (verified in Phase C4 - see "Storage path restrictions and DPAPI scope change" above), but are expected to be unreadable once accessed by a different identity (e.g. an installed LocalSystem service) - that cross-identity case remains untested and has no migration path if it does fail.
- The temp tunnel file's ACL restriction has a brief creation-to-restriction window - see `wireguard-connection-lifecycle.md`.
- Tests exercising DPAPI (`ProtectedData`) and Named Pipes require running on Windows; they were executed on this development machine and passed there.
- No real WireGuard connectivity has been tested - see `testing-guide.md` and `wireguard-connection-lifecycle.md`.
- Diagnostics' `PipeAvailable`/`AgentAvailable` are currently the same signal, both derived from a `GetStatusAsync` call - a dedicated pipe-only health check is a Deferred Item. See `diagnostics.md`.
