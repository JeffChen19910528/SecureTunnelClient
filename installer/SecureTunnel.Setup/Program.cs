using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using SecureTunnel.Setup;

// SecureTunnelSetup.exe              install or upgrade
// SecureTunnelSetup.exe /uninstall   remove (keeps imported configurations)
//   /removedata                      with /uninstall: also delete imported configurations
//   /silent                          no prompts, no "press any key"

var options = new SetupOptions(args);
var log = new SetupLog();

try
{
    if (options.Help)
    {
        log.Info("Usage: SecureTunnelSetup.exe [/uninstall [/removedata]] [/silent]");
        return 0;
    }

    if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
    {
        log.Error("Administrator rights are required. Right-click the file and choose \"Run as administrator\".");
        return Finish(options, 5);
    }

    var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0";
    var installer = new Installer(log, version);

    if (options.Uninstall)
    {
        // The installed copy of this program is inside the folder being deleted: continue from a temporary copy.
        if (installer.IsRunningFromInstallFolder() && !options.Relaunched)
        {
            return installer.RelaunchFromTemp(options);
        }

        installer.Uninstall(options.RemoveData);
        log.Info("SecureTunnel Connect was removed." + (options.RemoveData ? string.Empty : " Imported configurations were kept."));
        return Finish(options, 0);
    }

    installer.Install();
    log.Info($"SecureTunnel Connect {version} is installed. Start it from the Start menu (\"SecureTunnel Connect\").");
    return Finish(options, 0);
}
catch (Exception ex)
{
    log.Error("Setup failed: " + ex.Message);
    return Finish(options, 1);
}

static int Finish(SetupOptions options, int code)
{
    if (!options.Silent && Environment.UserInteractive && !Console.IsInputRedirected)
    {
        Console.WriteLine();
        Console.Write("Press any key to close...");
        Console.ReadKey(intercept: true);
    }

    return code;
}
