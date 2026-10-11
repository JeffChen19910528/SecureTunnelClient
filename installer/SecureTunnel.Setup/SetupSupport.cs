using System.Diagnostics;
using System.IO.Compression;

namespace SecureTunnel.Setup;

public sealed class SetupOptions
{
    public SetupOptions(string[] args)
    {
        Args = args.Where(a => !a.TrimStart('/', '-').Equals("relaunched", StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var raw in args)
        {
            switch (raw.TrimStart('/', '-').ToLowerInvariant())
            {
                case "uninstall": Uninstall = true; break;
                case "removedata": RemoveData = true; break;
                case "silent": case "quiet": case "s": case "q": Silent = true; break;
                case "relaunched": Relaunched = true; break;
                case "help": case "?": case "h": Help = true; break;
                default: throw new ArgumentException($"Unknown option '{raw}'. Use /help.");
            }
        }
    }

    /// <summary>The original arguments (without the internal /relaunched marker).</summary>
    public string[] Args { get; }

    public bool Uninstall { get; }
    public bool RemoveData { get; }
    public bool Silent { get; }
    public bool Relaunched { get; }
    public bool Help { get; }
}

public sealed class SetupLog
{
    public void Info(string message) => Console.WriteLine(message);

    public void Step(string message) => Console.WriteLine("  - " + message);

    public void Error(string message) => Console.Error.WriteLine("ERROR: " + message);
}

public static class PayloadExtractor
{
    /// <summary>
    /// Extracts the zip into <paramref name="destinationRoot"/>, overwriting existing files. Entry names may use
    /// either slash. Anything that would land outside the destination (".." or rooted names) is refused.
    /// </summary>
    public static int Extract(Stream zipStream, string destinationRoot)
    {
        var root = Path.GetFullPath(destinationRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var count = 0;
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/');
            if (relative.EndsWith('/'))
            {
                continue; // directory entry
            }

            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Refusing to extract '{entry.FullName}' outside the install folder.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            count++;
        }

        return count;
    }
}

public static class ProcessTools
{
    public static (int ExitCode, string Output) Run(string fileName, params string[] arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }
}
