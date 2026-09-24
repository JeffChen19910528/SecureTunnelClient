using System.Text.RegularExpressions;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Diagnostics;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;
using CoreValidation = SecureTunnel.Client.Core.Validation;

namespace SecureTunnel.Client.Infrastructure.WireGuard;

/// <summary>
/// <see cref="IWireGuardClientService"/> implementation. Never builds a
/// shell-concatenated command string (uses <see cref="IProcessRunner"/>,
/// which is backed by <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>)
/// and never invokes cmd.exe/powershell.exe as an intermediary. Private
/// key material is only ever written to a short-lived, ACL-restricted
/// temp file (never an argv token) and is deleted immediately after the
/// external process exits. A process exit code alone is never mapped to
/// <see cref="WireGuardOutcome.Connected"/> - only <see cref="GetStatusAsync"/>,
/// via <see cref="WireGuardDumpParser"/>, may report that outcome, and only
/// once a peer handshake has actually been observed.
/// </summary>
public sealed class WireGuardClientService : IWireGuardClientService
{
    private static readonly Regex SecretLine = new(@"(private-key|preshared-key)\b[^\r\n]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IProcessRunner _processRunner;
    private readonly IClientConfigurationParser _parser;
    private readonly IClientConfigurationStore _store;
    private readonly string _executableName;
    private readonly TimeSpan _operationTimeout;

    public WireGuardClientService(
        IProcessRunner processRunner,
        IClientConfigurationParser parser,
        IClientConfigurationStore store,
        string executableName = "wireguard.exe",
        TimeSpan? operationTimeout = null)
    {
        _processRunner = processRunner;
        _parser = parser;
        _store = store;
        _executableName = executableName;
        _operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<ImportResult> ImportConfiguration(string rawConfigText, CancellationToken cancellationToken)
    {
        var parsed = _parser.Parse(rawConfigText);
        if (!parsed.Success || parsed.Configuration is null || parsed.PrivateKey is null)
        {
            return new ImportResult
            {
                Outcome = WireGuardOutcome.InvalidConfiguration,
                Success = false,
                ErrorCode = "InvalidConfiguration",
                UserSafeMessage = "The provided configuration is invalid and could not be imported."
            };
        }

        var stored = await _store.SaveAsync(parsed.Configuration, parsed.PrivateKey, cancellationToken);
        if (!stored.Success)
        {
            return new ImportResult
            {
                Outcome = WireGuardOutcome.Unknown,
                Success = false,
                ErrorCode = stored.ErrorCode,
                UserSafeMessage = stored.Message
            };
        }

        return new ImportResult { Outcome = WireGuardOutcome.Unknown, Success = true };
    }

    public Task<CoreValidation.ValidationResult> ValidateConfiguration(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        var result = new CoreValidation.ValidationResult();

        if (string.IsNullOrWhiteSpace(configuration.InterfaceAddress))
        {
            result.AddError("InterfaceAddress", "Interface address is required.");
        }

        if (configuration.AllowedIPs.Count == 0)
        {
            result.AddError("AllowedIPs", "At least one AllowedIPs entry is required.");
        }

        if (string.IsNullOrWhiteSpace(configuration.PeerPublicKey))
        {
            result.AddError("PeerPublicKey", "Peer public key is required.");
        }

        if (string.IsNullOrWhiteSpace(configuration.PeerEndpoint))
        {
            result.AddError("PeerEndpoint", "Peer endpoint is required.");
        }

        return Task.FromResult(result);
    }

    public async Task<ConnectResult> ConnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!_processRunner.IsExecutableAvailable(_executableName))
        {
            return new ConnectResult
            {
                Outcome = WireGuardOutcome.WireGuardNotInstalled,
                Success = false,
                ErrorCode = "WireGuardNotInstalled",
                UserSafeMessage = "WireGuard is not installed on this computer."
            };
        }

        var loaded = await _store.LoadAsync(configuration.ClientId, cancellationToken);
        if (!loaded.Success || loaded.PrivateKey is null)
        {
            return new ConnectResult
            {
                Outcome = WireGuardOutcome.InvalidConfiguration,
                Success = false,
                ErrorCode = loaded.ErrorCode,
                UserSafeMessage = "Stored configuration could not be loaded."
            };
        }

        var tempConfigPath = Path.Combine(Path.GetTempPath(), $"stc-{Guid.NewGuid():N}.conf");

        try
        {
            await WriteTemporaryTunnelFile(tempConfigPath, loaded.Configuration!, loaded.PrivateKey, loaded.Configuration!.PresharedKey);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_operationTimeout);

            ProcessRunResult result;
            try
            {
                result = await _processRunner.RunAsync(_executableName, ["/installtunnelservice", tempConfigPath], timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ConnectResult { Outcome = WireGuardOutcome.ConnectionFailed, Success = false, ErrorCode = "OperationTimedOut", UserSafeMessage = "Connecting timed out." };
            }

            if (!result.Started)
            {
                return new ConnectResult { Outcome = WireGuardOutcome.ProcessStartFailed, Success = false, ErrorCode = "ProcessStartFailed", UserSafeMessage = "Unable to start the WireGuard tool." };
            }

            if (result.ExitCode == 5)
            {
                return new ConnectResult { Outcome = WireGuardOutcome.PermissionDenied, Success = false, ErrorCode = "PermissionDenied", UserSafeMessage = "Administrator permission is required to connect." };
            }

            if (result.ExitCode != 0)
            {
                return new ConnectResult { Outcome = WireGuardOutcome.ConnectionFailed, Success = false, ErrorCode = "ConnectionFailed", UserSafeMessage = "Failed to start the tunnel." };
            }

            // Exit code 0 only confirms the command was accepted, not that a
            // tunnel is up. Callers must call GetStatusAsync to confirm.
            return new ConnectResult { Outcome = WireGuardOutcome.Unknown, Success = true, UserSafeMessage = "Connect command sent; awaiting status confirmation." };
        }
        finally
        {
            if (File.Exists(tempConfigPath))
            {
                File.Delete(tempConfigPath);
            }
        }
    }

    public async Task<DisconnectResult> DisconnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!_processRunner.IsExecutableAvailable(_executableName))
        {
            return new DisconnectResult
            {
                Outcome = WireGuardOutcome.WireGuardNotInstalled,
                Success = false,
                ErrorCode = "WireGuardNotInstalled",
                UserSafeMessage = "WireGuard is not installed on this computer."
            };
        }

        // Idempotency: if the tunnel is already absent, do not invoke
        // uninstall at all - this also guarantees the operation only ever
        // targets the one tunnel this client owns, never "all tunnels".
        var currentStatus = await GetStatusAsync(configuration, cancellationToken);
        if (currentStatus.Outcome == WireGuardOutcome.Disconnected)
        {
            return new DisconnectResult { Outcome = WireGuardOutcome.Disconnected, Success = true };
        }

        var tunnelName = SanitizeTunnelName(configuration.ClientId);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_operationTimeout);

        ProcessRunResult result;
        try
        {
            result = await _processRunner.RunAsync(_executableName, ["/uninstalltunnelservice", tunnelName], timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DisconnectResult { Outcome = WireGuardOutcome.DisconnectFailed, Success = false, ErrorCode = "OperationTimedOut", UserSafeMessage = "Disconnecting timed out." };
        }

        if (!result.Started)
        {
            return new DisconnectResult { Outcome = WireGuardOutcome.ProcessStartFailed, Success = false, ErrorCode = "ProcessStartFailed", UserSafeMessage = "Unable to start the WireGuard tool." };
        }

        if (result.ExitCode == 5)
        {
            return new DisconnectResult { Outcome = WireGuardOutcome.PermissionDenied, Success = false, ErrorCode = "PermissionDenied", UserSafeMessage = "Administrator permission is required to disconnect." };
        }

        if (result.ExitCode != 0)
        {
            return new DisconnectResult { Outcome = WireGuardOutcome.DisconnectFailed, Success = false, ErrorCode = "DisconnectFailed", UserSafeMessage = Redact("Failed to stop the tunnel: " + result.StandardError) };
        }

        return new DisconnectResult { Outcome = WireGuardOutcome.Disconnected, Success = true };
    }

    public async Task<StatusResult> GetStatusAsync(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!_processRunner.IsExecutableAvailable(_executableName))
        {
            return new StatusResult
            {
                Outcome = WireGuardOutcome.WireGuardNotInstalled,
                Success = false,
                ObservedState = ClientState.Error,
                ErrorCode = "WireGuardNotInstalled",
                UserSafeMessage = "WireGuard is not installed on this computer."
            };
        }

        var tunnelName = SanitizeTunnelName(configuration.ClientId);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_operationTimeout);

        ProcessRunResult result;
        try
        {
            result = await _processRunner.RunAsync(_executableName, ["/dumptunnelservice", tunnelName], timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new StatusResult { Outcome = WireGuardOutcome.StatusUnavailable, Success = false, ObservedState = ClientState.Error, ErrorCode = "OperationTimedOut", UserSafeMessage = "Status query timed out." };
        }

        var outcome = WireGuardDumpParser.Parse(result.Started, result.ExitCode, result.StandardOutput);

        var observedState = outcome switch
        {
            WireGuardOutcome.Connected => ClientState.Connected,
            WireGuardOutcome.AwaitingHandshake => ClientState.AwaitingHandshake,
            WireGuardOutcome.InterfaceActive => ClientState.InterfaceActive,
            WireGuardOutcome.Disconnected => ClientState.Disconnected,
            _ => ClientState.Error
        };

        return new StatusResult
        {
            Outcome = outcome,
            Success = outcome != WireGuardOutcome.StatusUnavailable,
            ObservedState = observedState,
            ErrorCode = outcome == WireGuardOutcome.StatusUnavailable ? "StatusUnavailable" : null,
            UserSafeMessage = outcome == WireGuardOutcome.StatusUnavailable ? "Unable to determine tunnel status." : null
        };
    }

    private static async Task WriteTemporaryTunnelFile(
        string path,
        ClientConfiguration configuration,
        SecureTunnel.Client.Core.Security.SensitiveString privateKey,
        SecureTunnel.Client.Core.Security.SensitiveString? presharedKey)
    {
        var content = TunnelFileTextBuilder.Build(configuration, privateKey, presharedKey);

        await File.WriteAllTextAsync(path, content);

        try
        {
            File.SetAttributes(path, FileAttributes.NotContentIndexed);
            RestrictToCurrentUser(path);
        }
        catch (IOException)
        {
            // Best-effort hardening only; failure here must not block connect.
        }
    }

    /// <summary>
    /// Restricts the temp tunnel file to the current user. There is a
    /// brief window between file creation and this call during which the
    /// file carries the directory's inherited ACL - this is an accepted
    /// risk for a short-lived temp file, documented in
    /// docs/security-model.md, not a guarantee.
    /// </summary>
    private static void RestrictToCurrentUser(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or System.Security.Principal.IdentityNotMappedException)
        {
            // Best-effort hardening only; the file is still deleted in the
            // caller's finally block regardless of whether the ACL could
            // be tightened.
        }
    }

    private static string Redact(string text) =>
        SecretLine.Replace(DiagnosticResultFactory.Sanitize(text), "***REDACTED-LINE***");

    private static string SanitizeTunnelName(string clientId) =>
        new(clientId.Where(char.IsLetterOrDigit).ToArray());
}
