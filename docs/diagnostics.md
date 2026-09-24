# Diagnostics

## Status

- **Implemented & Tested & Passed:** `DiagnosticSnapshot`, `DiagnosticsViewModel`, and the redaction guarantees below - covered by `DiagnosticsViewModelTests`.
- **Not Implemented:** a dedicated pipe operation for fetching a richer, server-side diagnostic bundle - diagnostics are currently derived client-side from an ordinary `GetStatusAsync` call, not a new wire operation (kept the pipe's operation allow-list unchanged at four entries).

## What diagnostics cover

`SecureTunnel.Client.Core.Diagnostics.DiagnosticSnapshot` aggregates:

| Field | Source |
|---|---|
| `AgentAvailable` | Whether the last `GetStatusAsync` call's outcome was anything other than `ServiceUnavailable`/`PipeConnectionFailed`/`AccessDenied`. |
| `PipeAvailable` | Same signal as `AgentAvailable` in this phase (they are derived from the same call - a dedicated pipe-only health check is a Deferred Item). |
| `WireGuardExecutableAvailable` | Whether the outcome was anything other than `WireGuardNotInstalled`. |
| `LastValidation` | Reserved for a future `ValidateConfiguration`-specific diagnostic entry (`null` this phase). |
| `InterfaceState` | The `ClientState` observed by the last status check (`InterfaceActive`/`AwaitingHandshake`/`Connected`/`Disconnected`/etc.). |
| `LastOperation` | A `DiagnosticResult` (Operation, Result, ErrorCode, UserSafeMessage, TechnicalDetails, TimestampUtc, IsRetryable) for the most recent diagnostic-triggering call. |

`SecureTunnel.Connect.ViewModels.DiagnosticsViewModel.RefreshAsync` builds this snapshot by calling `IPrivilegedClientService.GetStatusAsync` and classifying the result - it never calls anything WireGuard- or storage-specific directly (that would violate the WPF-stays-unprivileged rule).

## Redaction guarantees

- `DiagnosticsViewModel.RefreshAsync` routes both `UserSafeMessage` and the synthesized `TechnicalDetails` through `DiagnosticResultFactory.Sanitize` (the same 42-44-character base64-shaped-token regex used everywhere else in this codebase) before they ever become part of a `DiagnosticResult` - this is true even though `PrivilegedClientResult.UserSafeMessage` is already expected to be hand-authored and safe; it is sanitized again here as defense in depth.
- `DiagnosticsViewModel.BuildClipboardText()` builds its output exclusively from the typed `DiagnosticSnapshot` fields (booleans, enums, and the already-sanitized `DiagnosticResult`) - there is no code path in this method that touches a `ClientConfiguration`, `SensitiveString`, or raw process output string.
- `DiagnosticsViewModelTests.BuildClipboardText_NeverContainsAKeyShapedToken` feeds a synthetic key-shaped token into a fake `UserSafeMessage` and asserts it never survives into the clipboard text.
- The WPF diagnostics panel (`MainWindow.xaml`) binds only to `Diagnostics.AgentAvailable`, `Diagnostics.WireGuardExecutableAvailable`, and `Diagnostics.LastOperationSummary` - none of which can carry key material by construction.

## What diagnostics deliberately never show

Per the Phase C3 spec: private keys, preshared keys, full raw configuration text, sensitive process arguments, and unredacted process output. None of these ever reach `DiagnosticSnapshot`, `DiagnosticResult`, or any XAML binding in this codebase - verified both by the type shapes involved (no key-typed property exists anywhere in the diagnostics path) and by the redaction tests above.

## Copy-to-clipboard

`DiagnosticsViewModel.CopyToClipboardCommand` calls `System.Windows.Clipboard.SetText(BuildClipboardText())`. This is a WPF UI-thread operation and is therefore **not exercised by the automated `DiagnosticsViewModelTests`** (which call `BuildClipboardText()` directly, not the command) - invoking `Clipboard` from a non-UI-thread test process is unreliable and out of scope for headless unit testing. Manually launching the WPF app and clicking "Copy to Clipboard" is a **Blocked (requires interactive session)** verification step for a later phase, consistent with the tray icon and file-picker pieces.
