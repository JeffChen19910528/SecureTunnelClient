using System.Security.AccessControl;
using System.Security.Principal;
using SecureTunnel.Client.Agent.Ipc;

namespace SecureTunnel.Client.Agent.Tests.Ipc;

/// <summary>
/// Unit-tested at the ACL-object level only (see
/// docs/security-model.md / docs/ipc-protocol.md): this verifies the
/// <see cref="System.IO.Pipes.PipeSecurity"/> object PipeSecurityFactory
/// constructs, not a real cross-identity access-denied scenario at
/// runtime, which is Blocked in this phase (requires a second Windows
/// identity/session not available here).
/// </summary>
public class PipeSecurityFactoryTests
{
    [Fact]
    public void Create_GrantsBuiltinUsers_ReadWrite()
    {
        var security = PipeSecurityFactory.Create();
        var builtinUsers = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, domainSid: null);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<System.IO.Pipes.PipeAccessRule>()
            .ToList();

        Assert.Contains(rules, r =>
            r.IdentityReference == builtinUsers &&
            r.AccessControlType == AccessControlType.Allow &&
            r.PipeAccessRights.HasFlag(System.IO.Pipes.PipeAccessRights.ReadWrite));
    }

    [Fact]
    public void Create_DeniesEveryone()
    {
        var security = PipeSecurityFactory.Create();
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<System.IO.Pipes.PipeAccessRule>()
            .ToList();

        Assert.Contains(rules, r => r.IdentityReference == everyone && r.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public void Create_DeniesAnonymousLogon()
    {
        var security = PipeSecurityFactory.Create();
        var anonymous = new SecurityIdentifier(WellKnownSidType.AnonymousSid, domainSid: null);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<System.IO.Pipes.PipeAccessRule>()
            .ToList();

        Assert.Contains(rules, r => r.IdentityReference == anonymous && r.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public void Create_DeniesNetwork()
    {
        var security = PipeSecurityFactory.Create();
        var network = new SecurityIdentifier(WellKnownSidType.NetworkSid, domainSid: null);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<System.IO.Pipes.PipeAccessRule>()
            .ToList();

        Assert.Contains(rules, r => r.IdentityReference == network && r.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public void Create_DoesNotGrantEveryoneAnyAccess()
    {
        var security = PipeSecurityFactory.Create();
        var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<System.IO.Pipes.PipeAccessRule>()
            .ToList();

        Assert.DoesNotContain(rules, r => r.IdentityReference == everyone && r.AccessControlType == AccessControlType.Allow);
    }
}
