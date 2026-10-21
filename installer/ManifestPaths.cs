using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KillerMCP.Setup
{
    // The installation manifest names every file setup checks, hashes and deletes. A name is used only when it
    // is a plain relative path below the installation folder: no drive, root, parent or empty step, no stream
    // name, no backslash, no device name, no step Windows would trim, and no link or junction on the way down,
    // so a changed manifest cannot reach outside it. Walking the folder never follows a link either.
    internal static class ManifestPaths
    {
        private static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

        internal static string? Resolve(string folder, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 260 || name == "manifest.json") return null;
            if (name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0 || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            string[] steps = name.Split('/');
            foreach (string step in steps)
            {
                if (step.Length == 0 || step == "." || step == ".." || step.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                // Windows trims a trailing dot or space, so "a." would open "a".
                if (step.EndsWith(".", StringComparison.Ordinal) || step.EndsWith(" ", StringComparison.Ordinal)) return null;
                string stem = step.Split('.')[0];
                if (DeviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) return null;
            }

            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            string path = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar.ToString(), steps)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;

            string current = root;
            for (int index = 0; index < steps.Length - 1; index++)
            {
                current = Path.Combine(current, steps[index]);
                if (Directory.Exists(current) && IsLink(current)) return null;
            }
            if (File.Exists(path) && IsLink(path)) return null;
            return path;
        }

        /// <summary>Every file below the folder, without descending into a linked folder.</summary>
        internal static IEnumerable<string> Files(string folder) => Walk(folder).Where(entry => entry.Kind == Kind.File).Select(entry => entry.Path);

        /// <summary>Every real folder below the folder, deepest last, without descending into a linked folder.</summary>
        internal static IEnumerable<string> Folders(string folder) => Walk(folder).Where(entry => entry.Kind == Kind.Folder).Select(entry => entry.Path);

        /// <summary>Every link or junction below the folder. Setup removes only the link itself.</summary>
        internal static IEnumerable<string> Links(string folder) => Walk(folder).Where(entry => entry.Kind == Kind.Link).Select(entry => entry.Path);

        private enum Kind { File, Folder, Link }

        private static IEnumerable<(string Path, Kind Kind)> Walk(string folder)
        {
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) yield return (entry, Kind.Link);
                    else if ((attributes & FileAttributes.Directory) != 0)
                    {
                        yield return (entry, Kind.Folder);
                        pending.Push(entry);
                    }
                    else yield return (entry, Kind.File);
                }
            }
        }

        private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}
