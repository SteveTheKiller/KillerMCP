using System;
using System.IO;

namespace KillerMCP.Setup
{
    // The installation manifest names every file setup checks, hashes and deletes. A name is used only when it
    // is a plain relative path below the installation folder: no drive, root, parent or empty step, no stream
    // name, no backslash, and no link or junction on the way down, so a changed manifest cannot reach outside it.
    internal static class ManifestPaths
    {
        internal static string? Resolve(string folder, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 260 || name == "manifest.json") return null;
            if (name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            string[] steps = name.Split('/');
            foreach (string step in steps)
                if (step.Length == 0 || step == "." || step == ".." || step.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;

            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar.ToString(), steps)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;

            string current = root;
            for (int index = 0; index < steps.Length - 1; index++)
            {
                current = Path.Combine(current, steps[index]);
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            return path;
        }
    }
}
