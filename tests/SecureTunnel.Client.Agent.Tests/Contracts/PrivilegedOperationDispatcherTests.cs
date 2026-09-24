using System.Reflection;
using SecureTunnel.Client.Agent.Contracts;
using SecureTunnel.Client.Agent.Service;
using SecureTunnel.Client.TestSupport;

namespace SecureTunnel.Client.Agent.Tests.Contracts;

public class PrivilegedOperationDispatcherTests
{
    [Fact]
    public void Dispatcher_OnlyExposesAllowListedOperations()
    {
        var methods = typeof(IPrivilegedOperationHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet();

        var expected = new HashSet<string>
        {
            nameof(IPrivilegedOperationHandler.HandleConnectAsync),
            nameof(IPrivilegedOperationHandler.HandleDisconnectAsync),
            nameof(IPrivilegedOperationHandler.HandleStatusAsync),
            nameof(IPrivilegedOperationHandler.HandleValidateConfigurationAsync)
        };

        Assert.Equal(expected, methods);
    }

    [Fact]
    public void Dispatcher_HasNoGenericExecuteMethod()
    {
        var methodNames = typeof(IPrivilegedOperationHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name);

        Assert.DoesNotContain(methodNames, name =>
            name.Contains("Execute", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RunCommand", StringComparison.OrdinalIgnoreCase));

        var dispatcherMethods = typeof(PrivilegedOperationDispatcher).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(dispatcherMethods, m => m.DeclaringType == typeof(PrivilegedOperationDispatcher));
    }

    [Fact]
    public async Task DispatchAsync_RoutesConnectRequest_ToConnectHandler()
    {
        var wireGuardService = new FakeWireGuardClientService();
        var store = new FakeClientConfigurationStore();
        var parser = new Infrastructure.Configuration.WireGuardConfigTextParser();
        var clientId = "client-1";
        await store.SaveAsync(
            new SecureTunnel.Client.Core.Models.ClientConfiguration
            {
                ClientId = clientId,
                GatewayName = "Gateway",
                GatewayEndpoint = "gateway.example.com:51820",
                InterfaceAddress = "10.0.0.2/32",
                PeerPublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=",
                PeerEndpoint = "gateway.example.com:51820",
                AllowedIPs = ["10.10.0.0/24"]
            },
            new SecureTunnel.Client.Core.Security.SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="),
            CancellationToken.None);

        var handler = new ClientAgentServiceFoundation(wireGuardService, store, parser);
        var dispatcher = new PrivilegedOperationDispatcher(handler);

        var response = await dispatcher.DispatchAsync(new ConnectRequest(clientId), CancellationToken.None);

        Assert.True(response.Success);
    }
}
