using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace SecureTunnel.Setup;

/// <summary>
/// Installs/removes SecureTunnel Connect using only Windows facilities: file copy, the Service Control Manager
/// (sc.exe), a Start menu shortcut and an "Apps &amp; features" registry entry.
/// </summary>
public sealed class Installer
{
    private const string ServiceName = "SecureTunnelAgent";
    private const string ProductName = "SecureTunnel Connect";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SecureTunnelConnect";
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(30);

    private readonly SetupLog _log;
    private readonly string _version;

    private readonly string _installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SecureTunnel");
    private readonly string _dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SecureTunnel");

    public Installer(SetupLog log, string version)
    {
        _log = log;
        _version = version;
    }

    private string AgentDir => Path.Combine(_installRoot, "Agent");
    private string ConnectDir => Path.Combine(_installRoot, "Connect");
    private string SetupDir => Path.Combine(_installRoot, "Setup");
    private string AgentExe => Path.Combine(AgentDir, "SecureTunnel.Client.Agent.exe");
    private string ConnectExe => Path.Combine(ConnectDir, "SecureTunnel.Connect.exe");
    private string InstalledSetupExe => Path.Combine(SetupDir, "SecureTunnelSetup.exe");
    private string ConfigsDir => Path.Combine(_dataRoot, "Agent", "configs");
    private string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ProductName + ".lnk");

    public bool IsRunningFromInstallFolder() =>
        Environment.ProcessPath is { } p && Path.GetFullPath(p).StartsWith(_installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public int RelaunchFromTemp(SetupOptions options)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SecureTunnelSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var copy = Path.Combine(tempDir, "SecureTunnelSetup.exe");
        File.Copy(Environment.ProcessPath!, copy);

        var psi = new ProcessStartInfo(copy) { UseShellExecute = false };
        foreach (var a in options.Args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("/relaunched");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not continue the uninstall.");
        process.WaitForExit();
        return process.ExitCode;
    }

    public void Install()
    {
        _log.Info($"Installing {ProductName} {_version} ...");

        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("This setup file was built without the application files.");

        var upgrading = ServiceExists();
        StopServiceAndApps();

        _log.Step("Copying files to " + _installRoot);
        var files = PayloadExtractor.Extract(payload, _installRoot);
        if (!File.Exists(AgentExe) || !File.Exists(ConnectExe) || !File.Exists(Path.Combine(AgentDir, "wireguard.dll")))
        {
            throw new InvalidOperationException("The installation package is incomplete (missing Agent, Connect or wireguard.dll).");
        }

        _log.Step($"{files} files copied");

        ProvisionDataDirectory();
        ConfigureService(upgrading);
        CopySelf();
        CreateShortcut();
        RegisterUninstall();

        _log.Step("Starting the SecureTunnel Agent service");
        using var controller = new ServiceController(ServiceName);
        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
    }

    public void Uninstall(bool removeData)
    {
        _log.Info($"Removing {ProductName} ...");

        StopServiceAndApps();

        if (ServiceExists())
        {
            _log.Step("Removing the SecureTunnel Agent service");
            var (code, output) = ProcessTools.Run("sc.exe", "delete", ServiceName);
            if (code != 0 && code != 1060 && code != 1072)
            {
                throw new InvalidOperationException($"Could not remove the service (sc delete exit {code}): {output}");
            }
        }

        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

        _log.Step("Deleting " + _installRoot);
        DeleteDirectoryWithRetry(_installRoot);

        if (removeData)
        {
            _log.Step("Deleting imported configurations in " + _dataRoot);
            DeleteDirectoryWithRetry(_dataRoot);
        }
    }

    private bool ServiceExists() => ServiceController.GetServices().Any(s => string.Equals(s.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase));

    private void StopServiceAndApps()
    {
        if (ServiceExists())
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.Status != ServiceControllerStatus.Stopped)
            {
                _log.Step("Stopping the SecureTunnel Agent service");
                try
                {
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ServiceProcess.TimeoutException)
                {
                    _log.Step("The service did not stop cleanly; ending its process");
                }
            }
        }

        foreach (var name in new[] { "SecureTunnel.Client.Agent", "SecureTunnel.Connect" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.MainModule?.FileName is { } path && path.StartsWith(_installRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(10_000);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // already exited, or not ours to inspect
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    private void ProvisionDataDirectory()
    {
        Directory.CreateDirectory(ConfigsDir);
        // Holds encrypted private keys: only SYSTEM and Administrators may touch it.
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        new DirectoryInfo(Path.Combine(_dataRoot, "Agent")).SetAccessControl(security);
    }

    private void ConfigureService(bool exists)
    {
        _log.Step(exists ? "Updating the SecureTunnel Agent service" : "Registering the SecureTunnel Agent service");
        var (code, output) = exists
            ? ProcessTools.Run("sc.exe", "config", ServiceName, "binPath=", AgentExe, "start=", "auto")
            : ProcessTools.Run("sc.exe", "create", ServiceName, "binPath=", AgentExe, "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "SecureTunnel Agent");
        if (code != 0)
        {
            throw new InvalidOperationException($"sc.exe could not register the service (exit {code}): {output}");
        }

        ProcessTools.Run("sc.exe", "description", ServiceName,
            "Runs the WireGuard connection for SecureTunnel Connect. Talks to the app only over a restricted local Named Pipe.");
        // Restart after the first two failures, then leave it alone.
        ProcessTools.Run("sc.exe", "failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/5000/none/0");
    }

    private void CopySelf()
    {
        Directory.CreateDirectory(SetupDir);
        if (Environment.ProcessPath is { } self && !string.Equals(Path.GetFullPath(self), InstalledSetupExe, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(self, InstalledSetupExe, overwrite: true);
        }
    }

    private void CreateShortcut()
    {
        _log.Step("Creating the Start menu shortcut");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            _log.Step("(Start menu shortcut skipped: Windows Script Host is unavailable)");
            return;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(ShortcutPath);
        link.TargetPath = ConnectExe;
        link.WorkingDirectory = ConnectDir;
        link.Description = ProductName;
        link.Save();
    }

    private void RegisterUninstall()
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", _version);
        key.SetValue("Publisher", "SecureTunnel");
        key.SetValue("InstallLocation", _installRoot);
        key.SetValue("DisplayIcon", ConnectExe);
        key.SetValue("UninstallString", $"\"{InstalledSetupExe}\" /uninstall");
        key.SetValue("QuietUninstallString", $"\"{InstalledSetupExe}\" /uninstall /silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)(DirSize(_installRoot) / 1024), RegistryValueKind.DWord);
    }

    private static long DirSize(string path) =>
        Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 10 && Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }

        if (Directory.Exists(path))
        {
            throw new IOException($"Could not delete '{path}'. Close anything using it and run the uninstall again.");
        }
    }
}
