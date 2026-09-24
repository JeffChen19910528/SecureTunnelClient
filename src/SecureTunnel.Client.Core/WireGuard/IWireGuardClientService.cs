using SecureTunnel.Client.Core.Models;

namespace SecureTunnel.Client.Core.WireGuard;

/// <summary>
/// Abstraction over the local WireGuard tooling. Implementations must not
/// assume WireGuard is installed, must never build shell command strings
/// (use ProcessStartInfo.ArgumentList), and must never place private key
/// material in command-line arguments.
/// </summary>
public interface IWireGuardClientService
{
    Task<ImportResult> ImportConfiguration(string rawConfigText, CancellationToken cancellationToken);

    Task<Validation.ValidationResult> ValidateConfiguration(ClientConfiguration configuration, CancellationToken cancellationToken);

    Task<ConnectResult> ConnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken);

    Task<DisconnectResult> DisconnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken);

    Task<StatusResult> GetStatusAsync(ClientConfiguration configuration, CancellationToken cancellationToken);
}
