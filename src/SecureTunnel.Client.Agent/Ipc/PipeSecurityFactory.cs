using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SecureTunnel.Client.Agent.Ipc;

/// <summary>
/// Builds the access control list applied to the Named Pipe server.
///
/// Rule set (see docs/windows-service-boundary.md's Service Identity
/// Rationale and docs/ipc-protocol.md for full context): grants
/// <c>ReadWrite</c> to <c>BUILTIN\Users</c> - broad enough that any local
/// desktop user account can operate the single-user prototype client,
/// while still excluding services, remote callers, and anonymous
/// connections. Explicitly denies <c>Everyone</c>, <c>ANONYMOUS LOGON</c>,
/// and <c>NT AUTHORITY\NETWORK</c>.
///
/// ACCEPTED RISK, not overclaimed: because the Agent is designed to run
/// as LocalSystem (see the rationale doc) while the WPF client runs
/// unelevated as an arbitrary interactive user, a per-importing-user SID
/// allow-list would require an install-time configuration step that does
/// not exist yet - deferred to a later phase. Until then, any local user
/// account (not just the one who imported a configuration) can issue
/// pipe operations against whatever configuration is currently stored.
/// This has been verified only at the ACL-object-construction level
/// (<c>PipeSecurityFactoryTests</c>) - there is no automated or manual
/// test in this phase that proves a genuinely different Windows identity
/// is denied at runtime, because doing so requires a second logon session
/// not available in this environment.
/// </summary>
public static class PipeSecurityFactory
{
    public static PipeSecurity Create()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, domainSid: null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AnonymousSid, domainSid: null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, domainSid: null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        return security;
    }
}
