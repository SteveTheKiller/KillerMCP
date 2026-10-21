using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KillerMCP.Setup;

// Setup reads, hashes and deletes only what is inside the installation folder, whatever the manifest lists
// and whatever links sit in the folder. These checks run on .NET Framework 4.8, as setup does.
var root = Path.Combine(Path.GetTempPath(), "killermcp-setup-checks-" + Guid.NewGuid().ToString("N"));
var installed = Path.Combine(root, "installed");
var outside = Path.Combine(root, "outside");
Directory.CreateDirectory(Path.Combine(installed, "sub"));
Directory.CreateDirectory(Path.Combine(outside, "empty"));
File.WriteAllText(Path.Combine(installed, "KillerMCP.exe"), "fixture");
File.WriteAllText(Path.Combine(installed, "sub", "a.dll"), "fixture");
File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
try
{
    Equal(Path.Combine(installed, "KillerMCP.exe"), ManifestPaths.Resolve(installed, "KillerMCP.exe"));
    Equal(Path.Combine(installed, "sub", "a.dll"), ManifestPaths.Resolve(installed, "sub/a.dll"));
    // Windows drops trailing dots and spaces from a name, so "a." would reach "a"; such names are refused.
    foreach (var unsafeName in new[] { "", "../outside.exe", "sub/../../outside.exe", "/rooted.exe", "C:/Windows/notepad.exe", "C:relative.exe",
        "sub\\a.dll", "a.exe:stream", "manifest.json", "sub//a.dll", "./a.exe", "sub/.", "sub/a.", "sub/a ", "...", "sub /a.dll", "con", "sub/nul.txt" })
        Equal<string?>(null, ManifestPaths.Resolve(installed, unsafeName));

    // A junction to a folder outside, and a file link to a file outside, are never followed.
    Junction(Path.Combine(installed, "joined"), outside);
    Equal<string?>(null, ManifestPaths.Resolve(installed, "joined/secret.txt"));
    var linkedFile = Path.Combine(installed, "linked.txt");
    if (FileLink(linkedFile, Path.Combine(outside, "secret.txt")))
    {
        Equal<string?>(null, ManifestPaths.Resolve(installed, "linked.txt"));
        File.Delete(linkedFile);
    }
    else
    {
        Console.WriteLine("SKIP file link check: this account cannot create symbolic links.");
    }

    // Walking the folder lists links as links and never descends into them.
    var files = ManifestPaths.Files(installed).Select(path => path.Substring(installed.Length + 1)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    Equal("KillerMCP.exe|sub\\a.dll", string.Join("|", files));
    var links = ManifestPaths.Links(installed).ToArray();
    Equal(1, links.Length);
    Equal(Path.Combine(installed, "joined"), links[0]);
    var folders = ManifestPaths.Folders(installed).ToArray();
    Equal(false, folders.Any(folder => folder.StartsWith(outside, StringComparison.OrdinalIgnoreCase) || folder.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0));
    Equal(true, folders.Contains(Path.Combine(installed, "sub")));
    Equal(false, folders.Contains(Path.Combine(installed, "joined")));

    // A junction that points back at its own folder does not make the walk loop.
    Junction(Path.Combine(installed, "sub", "loop"), installed);
    Equal(2, ManifestPaths.Links(installed).Count());
    Equal(true, Directory.Exists(Path.Combine(outside, "empty")));
    Console.WriteLine("PASS setup stays inside the installation folder on .NET Framework");
    return 0;
}
finally
{
    // Links are removed first so the cleanup never reaches through them.
    foreach (var link in new[] { Path.Combine(installed, "sub", "loop"), Path.Combine(installed, "joined") })
        if (Directory.Exists(link)) Directory.Delete(link);
    Directory.Delete(root, true);
}

static void Junction(string link, string target)
{
    using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException("Could not create the junction fixture.");
}

static bool FileLink(string link, string target)
{
    using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
    process.WaitForExit();
    return process.ExitCode == 0;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, received {actual}.");
}
