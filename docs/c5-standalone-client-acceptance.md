# Phase C5 — Standalone Client Hardening & Local Acceptance

## Status

**COMPLETED** for standalone-client scope. This phase deliberately does not require the SecureTunnel Gateway, a real Windows Service installation, or real WireGuard connectivity - all of those remain exactly as documented in `docs/c4-acceptance-report.md` (Blocked/Not Verified). What follows is what standalone-client verification *can* cover, and it now does.

## Verified locally

- **Client configuration parsing** - `WireGuardConfigTextParserTests` (unchanged this phase, still passing).
- **State machine** - `ClientStateMachineTests`, hardened this phase with explicit coverage for: repeated Connect while already connecting/connected/disconnecting is illegal (no conflicting second operation possible), repeated Disconnect while not connected is illegal, `ServiceUnavailable` is reachable only via `AgentUnavailable` and can never be conflated with a WireGuard-level `ConnectionFailed`. 66 total state-machine-related assertions across valid/invalid transition theories.
- **IPC protocol / service boundary logic** - `PipeSessionHandlerTests`, `NamedPipeIntegrationTests`, `PrivilegedOperationDispatcherTests`, `ClientAgentServiceFoundationTests` (unchanged this phase, all still passing; already covered the Part 4 checklist from earlier phases).
- **WireGuard adapter behavior through test doubles** - `FakeWireGuardClientService` was rebuilt this phase into a named-scenario test double (`FakeScenario` enum, 14 members) covering every scenario Part 2 asked for: `WireGuardNotInstalled`, `InvalidConfiguration`, `PermissionDenied`, `ProcessStartFailure`, `ProcessTimeout`, `ProcessTimeoutThenKilled`, `InterfaceCreationSuccess`, `InterfaceActiveAwaitingHandshake`, `HandshakeSuccess`, `StatusUnavailable`, `DisconnectSuccess`, `DisconnectFailure`, `RepeatedDisconnectSafe`, `Disconnected`. Every scenario is proven to configure the double correctly by `FakeWireGuardClientServiceScenarioTests` (14 tests). The type is explicitly labeled `TEST DOUBLE - SIMULATED, LOCAL ACCEPTANCE ONLY` in its XML doc and never reports real network connectivity - it is a plain C# object with no process/network access at all.
- **DPAPI scenarios actually tested** - real encrypt/decrypt round trips (both `CurrentUser` and `LocalMachine` scope), the Phase C4 same-identity cross-scope finding, fail-closed corrupted-data handling, atomic-write-failure handling, and "no silent overwrite" (all pre-existing, re-confirmed passing this phase - no new DPAPI code was needed since Part 9's checklist was already satisfied by C3/C4 work).
- **WPF ViewModel behavior** - `MainViewModelTests`, `ImportViewModelTests`, `DiagnosticsViewModelTests`. Extended this phase with explicit display-distinction tests: `InterfaceActive` and `AwaitingHandshake` are each proven to never display as `Connected`; `Connected` is only ever set from an explicit `ObservedState` value the fake supplies (never inferred); a `ConnectionFailed`/timeout outcome's status text is proven to surface the underlying message (see "Real finding" below) without claiming success; repeated `DisconnectAsync` calls are proven safe (no throw, consistent end state, call count tracked).
- **Security checks** - re-confirmed: no shell command concatenation (`grep -rn "\.Arguments\s*=" src/` clean), no generic command execution, redaction of key-shaped tokens in diagnostics/exceptions/process output, temp tunnel file cleanup **including on the timeout/cancellation path specifically** (new test this phase - see Part K below), WPF `app.manifest` still `asInvoker`, `SecureTunnel.Connect.csproj` still has no reference to `SecureTunnel.Client.Agent`.
- **Build and test results** - Debug and Release builds both clean (0 warnings/errors); full regression suite re-run and green.

## Not verified (unchanged from Phase C4, restated for completeness)

- Real Windows Service installation, start/stop/restart, or runtime identity.
- Real LocalSystem runtime behavior.
- Real WireGuard tunnel establishment.
- Real WireGuard handshake.
- Real SecureTunnel Gateway connectivity or protected-resource access.
- Cross-identity Named Pipe ACL denial (still requires a second Windows identity, unavailable in this environment).

None of the above were attempted this phase - Phase C5 is scoped to standalone-client verification only, per its own instructions.

## A real finding this phase (not a simulated result)

While extending `MainViewModelTests` for the Part 5 display-distinction checklist, a genuine (if minor) UX defect was found and fixed: `MainViewModel.StatusText` mapped `WireGuardOutcome.ConnectionFailed`/`DisconnectFailed` to a fixed generic string ("Connection failed."/"Disconnect failed."), discarding the more specific `UserSafeMessage` the Agent/proxy actually supplied (e.g. "Connecting timed out."). This meant a user would see a generic failure message even when a more specific, still-safe explanation was available. Fixed in `src/SecureTunnel.Connect/ViewModels/MainViewModel.cs` to append the message when present, and covered by `ConnectAsync_TimeoutOutcome_StatusTextDoesNotClaimConnected`. This is a real code change with a real regression test, not a documentation-only correction (contrast with Phase C4's DPAPI finding, which corrected a doc claim without a code defect).

## Part 8 review: Named Pipe security (no redesign, evaluation only)

Per this phase's explicit instruction not to redesign the `BUILTIN\Users` ACL without a concrete new issue, the following evaluation was performed by re-reading the current implementation (no code changed):

1. **What operations are available through IPC?** Exactly four: Connect, Disconnect, Status, ValidateConfiguration (`PipeProtocol.AllowedOperations`, enforced by `PipeSessionHandler` before any dispatch).
2. **Which require authorization?** All four are gated by the pipe's ACL itself (connection-level, not per-operation) - there is no per-operation authorization layer beyond "can this identity open the pipe at all."
3. **Can a local user invoke privileged WireGuard actions?** Yes, any member of `BUILTIN\Users` who can reach the pipe can issue Connect/Disconnect/Status for whatever configuration is currently stored - this is the accepted risk restated in every phase since C3, unchanged.
4. **Is the request associated with the caller identity?** No - `PipeRequestEnvelope` carries only a `ClientId` chosen by the caller, not a cryptographic or OS-level identity claim. This is a real, unresolved gap, but resolving it would require either a per-user SID allow-list (needs a real install to test, per C4) or per-request identity binding (e.g. impersonation-based checks), both explicitly Deferred, not attempted this phase since it would be redesign work outside C5's "evaluate, do not redesign" instruction.
5. **Are malformed requests fail-closed?** Yes - re-confirmed by `PipeSessionHandlerTests`.
6. **Are unknown operations rejected?** Yes - re-confirmed.
7. **Are secrets excluded from IPC?** Yes - re-confirmed (`PipeResponseEnvelope`'s `Config*` fields are non-secret by construction; no request/response field carries a key).
8. **Is the current ACL broader than necessary?** Yes, by design/accepted-risk (see item 3) - unchanged.
9. **What can be improved without a second identity?** Nothing concrete was identified this phase that doesn't require either a real install (per-user SID enforcement) or a broader redesign (per-request identity binding) - both correctly left as Deferred Items rather than attempted speculatively.

**Conclusion: no redesign performed, consistent with the phase's instruction.** The `BUILTIN\Users` ACL remains an Accepted Risk, now with a slightly more precise articulation of *why* (no per-request identity binding exists) rather than just *that* it's broad.

## Test categorization (Part 10)

| Category | Examples | Count this phase |
|---|---|---|
| Unit | State machine theories, `WireGuardDumpParser`, ViewModel tests | Majority of the 166 total |
| Test Double | `FakeWireGuardClientServiceScenarioTests` (14 new) | 14 |
| Local Integration / In-process IPC | `NamedPipeIntegrationTests`, `NamedPipePrivilegedClientProxyTests` (pre-existing, re-run) | 0 new this phase |
| Static Security Check | `grep` verification (manual, not a `dotnet test` entry), redaction tests | 0 new this phase |
| Real Windows API | `WireGuardProcessRunnerTests` (real child process, pre-existing), DPAPI round trips (pre-existing) | 0 new this phase |
| Manual UI | Tray icon, file dialog, clipboard - still **Blocked**, unchanged | 0 |
| Blocked | Everything in "Not verified" above | 0 (no test attempted for these) |

**28 new tests this phase** (166 total, up from 138): 9 Core (state machine hardening), 14 Agent (scenario test double verification), 4 Connect (WPF display distinctions + repeated-disconnect safety), 1 Infrastructure (timeout-path temp-file cleanup). No test was added solely to inflate the count - each corresponds to an explicit Part 2/3/5/6 checklist item that was not yet covered.
