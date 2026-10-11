using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SecureTunnel.Client.Agent.Ipc;

/// <summary>
/// Builds the access control list applied to the Named Pipe server.
///
/// Allows <c>BUILTIN\Users</c> to read/write, so the unelevated desktop app can talk to the Agent, and gives
/// <c>SYSTEM</c> and <c>BUILTIN\Administrators</c> full control. <c>ANONYMOUS LOGON</c> and
/// <c>NT AUTHORITY\NETWORK</c> are explicitly denied, which keeps remote and anonymous callers out.
///
/// There must be no Deny entry for <c>Everyone</c>: a Deny always wins over an Allow, so it would also lock out
/// <c>BUILTIN\Users</c> and nobody could connect. Everyone not allowed above is already refused because no Allow
/// entry exists for them.
///
/// Any local user account can therefore issue pipe operations against the stored configuration; the pipe does
/// not distinguish between users of the same PC.
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

        // The service identity and administrators keep full control. A Deny for Everyone (World) must NOT be
        // used: Deny ACEs win over Allow ACEs, so it also locks out BUILTIN\Users and nobody could connect.
        // Everyone not explicitly allowed above is already denied by the absence of an Allow rule.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

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
