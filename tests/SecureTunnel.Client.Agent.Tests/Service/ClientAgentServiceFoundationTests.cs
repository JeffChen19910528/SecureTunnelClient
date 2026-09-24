using System.Reflection;
using SecureTunnel.Client.Agent.Contracts;
using SecureTunnel.Client.Agent.Service;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.Infrastructure.Configuration;
using SecureTunnel.Client.TestSupport;

namespace SecureTunnel.Client.Agent.Tests.Service;

public class ClientAgentServiceFoundationTests
{
    private static ClientConfiguration SampleConfiguration(string clientId) => new()
    {
        ClientId = clientId,
        GatewayName = "Gateway",
        GatewayEndpoint = "gateway.example.com:51820",
        InterfaceAddress = "10.0.0.2/32",
        PeerPublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=",
        PeerEndpoint = "gateway.example.com:51820",
        AllowedIPs = ["10.10.0.0/24"]
    };

    [Fact]
    public async Task Handle_ValidConfiguration_Succeeds()
    {
        var store = new FakeClientConfigurationStore();
        var clientId = "client-1";
        await store.SaveAsync(SampleConfiguration(clientId), new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="), CancellationToken.None);

        var handler = new ClientAgentServiceFoundation(new FakeWireGuardClientService(), store, new WireGuardConfigTextParser());

        var response = await handler.HandleStatusAsync(new StatusRequest(clientId), CancellationToken.None);

        Assert.True(response.Success);
    }

    [Fact]
    public void Handle_NoArbitraryCommandExecution()
    {
        var methodNames = typeof(IPrivilegedOperationHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain(methodNames, name =>
            name.Contains("Execute", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RunShell", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RunCommand", StringComparison.OrdinalIgnoreCase));

        var implementationMethods = typeof(ClientAgentServiceFoundation)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(ClientAgentServiceFoundation))
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(4, implementationMethods.Count);
    }

    [Fact]
    public async Task HandleValidateConfigurationAsync_ValidConfig_PopulatesConfigurationSummary_AndSavesIt()
    {
        var store = new FakeClientConfigurationStore();
        var handler = new ClientAgentServiceFoundation(new FakeWireGuardClientService(), store, new WireGuardConfigTextParser());
        const string privateKey = "hyCu4faV33FeOhqiRoy9bRSV30qH/XO0K98N3Mg7uD8=";
        const string publicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=";
        var rawConfig = $"""
            [Interface]
            PrivateKey = {privateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {publicKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var response = await handler.HandleValidateConfigurationAsync(new ValidateConfigurationRequest(string.Empty, rawConfig), CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Configuration);
        Assert.NotEmpty(response.Configuration!.ClientId);
        Assert.Equal("10.0.0.2/32", response.Configuration.InterfaceAddress);

        var reloaded = await store.LoadAsync(response.Configuration.ClientId, CancellationToken.None);
        Assert.True(reloaded.Success);
    }

    [Fact]
    public void ConfigurationSummary_HasNoKeyTypedProperty()
    {
        var propertyNames = typeof(ConfigurationSummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        Assert.DoesNotContain(propertyNames, name => name.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }
}
