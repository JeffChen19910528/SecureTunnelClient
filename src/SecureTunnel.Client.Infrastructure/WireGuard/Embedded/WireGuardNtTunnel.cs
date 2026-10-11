using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.Infrastructure.WireGuard.Embedded;

/// <summary>
/// <see cref="IWireGuardNativeTunnel"/> backed by the wireguard.dll (wireguard-nt) shipped next to the
/// Agent, so no separate WireGuard installation is needed. The adapter exists only while this process
/// holds its handle: closing the handle (Down) or the Agent exiting/crashing removes the adapter, which
/// fails closed. Requires the caller to be elevated (the Agent runs as a Windows Service).
/// Only the public wireguard.h API is used, as required by the wireguard-nt licence.
/// IP addresses, routes and DNS are applied with the OS-provided netsh.exe.
/// </summary>
public sealed class WireGuardNtTunnel : IWireGuardNativeTunnel, IDisposable
{
    private const string Dll = "wireguard.dll";
    private const uint ErrorMoreData = 234;
    private static readonly TimeSpan NetshTimeout = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly Dictionary<string, IntPtr> _adapters = new(StringComparer.OrdinalIgnoreCase);

    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr WireGuardCreateAdapter(string name, string tunnelType, ref Guid requestedGuid);

    [DllImport(Dll, SetLastError = true)]
    private static extern void WireGuardCloseAdapter(IntPtr adapter);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WireGuardSetConfiguration(IntPtr adapter, byte[] config, uint bytes);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WireGuardGetConfiguration(IntPtr adapter, byte[]? config, ref uint bytes);

    [DllImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WireGuardSetAdapterState(IntPtr adapter, int state);

    public bool IsAvailable(out string? reason)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            reason = "The embedded WireGuard engine is only bundled for 64-bit Windows.";
            return false;
        }

        var path = Path.Combine(AppContext.BaseDirectory, Dll);
        if (!NativeLibrary.TryLoad(path, out var handle))
        {
            reason = "The bundled wireguard.dll could not be loaded from the application folder.";
            return false;
        }

        NativeLibrary.Free(handle);
        reason = null;
        return true;
    }

    public NativeTunnelResult Up(string adapterName, ClientConfiguration configuration, SensitiveString privateKey, SensitiveString? presharedKey)
    {
        byte[] buffer;
        (IPNetwork Network, IPAddress Address, int Prefix) address;
        List<IPNetwork> allowed;
        try
        {
            allowed = configuration.AllowedIPs.Select(a => WireGuardNtConfigBuilder.ParseCidr(a).Network).ToList();
            address = WireGuardNtConfigBuilder.ParseCidr(configuration.InterfaceAddress.Split(',')[0]);
            buffer = WireGuardNtConfigBuilder.Build(
                WireGuardNtConfigBuilder.DecodeKey(privateKey.Reveal()),
                WireGuardNtConfigBuilder.DecodeKey(configuration.PeerPublicKey),
                presharedKey is null ? null : WireGuardNtConfigBuilder.DecodeKey(presharedKey.Reveal()),
                WireGuardNtConfigBuilder.ParseEndpoint(configuration.PeerEndpoint),
                (ushort)Math.Clamp(configuration.PersistentKeepalive ?? 0, 0, ushort.MaxValue),
                allowed);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return new NativeTunnelResult(false, "InvalidConfiguration", ex.Message);
        }

        if (allowed.Any(n => n.PrefixLength == 0))
        {
            // A full tunnel needs an endpoint-bypass route to avoid routing the handshake into the tunnel.
            return new NativeTunnelResult(false, "FullTunnelUnsupported", "AllowedIPs of 0.0.0.0/0 or ::/0 are not supported; list the protected networks instead.");
        }

        lock (_gate)
        {
            if (_adapters.ContainsKey(adapterName))
            {
                return new NativeTunnelResult(true);
            }

            var guid = WireGuardNtConfigBuilder.AdapterGuidFor(adapterName);
            var adapter = WireGuardCreateAdapter(adapterName, "SecureTunnel", ref guid);
            if (adapter == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                return new NativeTunnelResult(false, err == 5 ? "PermissionDenied" : "AdapterCreateFailed", $"WireGuardCreateAdapter failed (Win32 {err}).");
            }

            try
            {
                if (!WireGuardSetConfiguration(adapter, buffer, (uint)buffer.Length))
                {
                    return Fail(adapter, "SetConfigurationFailed", Marshal.GetLastWin32Error());
                }

                if (!WireGuardSetAdapterState(adapter, 1))
                {
                    return Fail(adapter, "AdapterUpFailed", Marshal.GetLastWin32Error());
                }

                var netsh = ConfigureNetwork(adapterName, address, allowed, configuration);
                if (!netsh.Success)
                {
                    WireGuardCloseAdapter(adapter);
                    return netsh;
                }

                _adapters[adapterName] = adapter;
                return new NativeTunnelResult(true);
            }
            finally
            {
                Array.Clear(buffer); // contains the private key
            }
        }

        static NativeTunnelResult Fail(IntPtr adapter, string code, int win32)
        {
            WireGuardCloseAdapter(adapter);
            return new NativeTunnelResult(false, code, $"{code} (Win32 {win32}).");
        }
    }

    public NativeTunnelResult Down(string adapterName)
    {
        lock (_gate)
        {
            if (_adapters.Remove(adapterName, out var adapter))
            {
                WireGuardCloseAdapter(adapter); // removes the adapter and its routes
            }

            return new NativeTunnelResult(true);
        }
    }

    public NativeTunnelSnapshot? Query(string adapterName)
    {
        lock (_gate)
        {
            if (!_adapters.TryGetValue(adapterName, out var adapter))
            {
                return null;
            }

            uint size = 0;
            WireGuardGetConfiguration(adapter, null, ref size);
            if (Marshal.GetLastWin32Error() != ErrorMoreData || size == 0)
            {
                return new NativeTunnelSnapshot(null, 0, 0);
            }

            var buffer = new byte[size];
            if (!WireGuardGetConfiguration(adapter, buffer, ref size))
            {
                return new NativeTunnelSnapshot(null, 0, 0);
            }

            var (handshake, rx, tx) = WireGuardNtConfigBuilder.ReadFirstPeer(buffer);
            return new NativeTunnelSnapshot(handshake, rx, tx);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var adapter in _adapters.Values)
            {
                WireGuardCloseAdapter(adapter);
            }

            _adapters.Clear();
        }
    }

    private static NativeTunnelResult ConfigureNetwork(string name, (IPNetwork Network, IPAddress Address, int Prefix) address, List<IPNetwork> allowed, ClientConfiguration configuration)
    {
        var ctx = address.Address.AddressFamily == AddressFamily.InterNetwork ? "ipv4" : "ipv6";
        var steps = new List<string[]>();
        steps.Add(["interface", ctx, "add", "address", $"name={name}", $"address={address.Address}/{address.Prefix}", "store=active"]);

        if (configuration.Mtu is { } mtu)
        {
            steps.Add(["interface", ctx, "set", "subinterface", name, $"mtu={mtu}", "store=active"]);
        }

        foreach (var net in allowed)
        {
            var routeCtx = net.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? "ipv4" : "ipv6";
            var nextHop = routeCtx == "ipv4" ? "0.0.0.0" : "::";
            steps.Add(["interface", routeCtx, "add", "route", $"prefix={net}", $"interface={name}", $"nexthop={nextHop}", "metric=5", "store=active"]);
        }

        if (configuration.DnsServers is { Count: > 0 })
        {
            for (var i = 0; i < configuration.DnsServers.Count; i++)
            {
                var dns = configuration.DnsServers[i];
                var dnsCtx = IPAddress.TryParse(dns, out var d) && d.AddressFamily == AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4";
                if (i == 0)
                {
                    steps.Add(["interface", dnsCtx, "set", "dnsservers", $"name={name}", "source=static", $"address={dns}", "validate=no"]);
                }
                else
                {
                    steps.Add(["interface", dnsCtx, "add", "dnsservers", $"name={name}", $"address={dns}", "validate=no"]);
                }
            }
        }

        foreach (var step in steps)
        {
            var result = RunNetsh(step);
            if (!result.Success)
            {
                return result;
            }
        }

        return new NativeTunnelResult(true);
    }

    private static NativeTunnelResult RunNetsh(string[] arguments)
    {
        // The freshly created adapter can take a moment to become addressable; retry briefly.
        string last = string.Empty;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in arguments) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new NativeTunnelResult(false, "ProcessStartFailed", "netsh.exe could not be started.");
            }

            if (!process.WaitForExit(NetshTimeout))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new NativeTunnelResult(false, "NetworkConfigTimedOut", "netsh.exe timed out.");
            }

            if (process.ExitCode == 0)
            {
                return new NativeTunnelResult(true);
            }

            last = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
            Thread.Sleep(300);
        }

        return new NativeTunnelResult(false, "NetworkConfigFailed", $"netsh {string.Join(' ', arguments.Take(4))} failed: {last}");
    }
}
