namespace SecureTunnel.Client.Infrastructure.WireGuard;

public sealed record ProcessRunResult(bool Started, int ExitCode, string StandardOutput, string StandardError);
