using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureTunnel.Client.Agent.Contracts;
using SecureTunnel.Client.Agent.Ipc;
using SecureTunnel.Client.Agent.Service;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.Infrastructure.Configuration;
using SecureTunnel.Client.Infrastructure.Storage;
using SecureTunnel.Client.Infrastructure.WireGuard;
using SecureTunnel.Client.Infrastructure.WireGuard.Embedded;

var builder = Host.CreateApplicationBuilder(args);

// The Windows Service Control Manager name; must match
// deploy/install-service.ps1's -Name and docs/windows-service-installation.md.
// Display name and description are SCM-level metadata set at install time
// by that script (AddWindowsService's options do not expose them).
builder.Services.AddWindowsService(options => options.ServiceName = "SecureTunnelAgent");

builder.Services.AddSingleton<IClientConfigurationParser, WireGuardConfigTextParser>();
builder.Services.AddSingleton<IClientConfigurationStore, DpapiClientConfigurationStore>();
builder.Services.AddSingleton<IProcessRunner, WireGuardProcessRunner>();
// Default: the bundled wireguard-nt engine (nothing else to install). Set Tunnel:Engine=WireGuardExe
// (or env Tunnel__Engine) to drive an installed WireGuard for Windows instead.
if (string.Equals(builder.Configuration["Tunnel:Engine"], "WireGuardExe", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IWireGuardClientService, WireGuardClientService>();
}
else
{
    builder.Services.AddSingleton<IWireGuardNativeTunnel, WireGuardNtTunnel>();
    builder.Services.AddSingleton<IWireGuardClientService, EmbeddedWireGuardClientService>();
}
builder.Services.AddSingleton<IPrivilegedOperationHandler, ClientAgentServiceFoundation>();
builder.Services.AddSingleton<IPrivilegedOperationDispatcher, PrivilegedOperationDispatcher>();
builder.Services.AddSingleton<PipeSessionHandler>();
builder.Services.AddHostedService<NamedPipeHostedService>();

var host = builder.Build();
host.Run();
