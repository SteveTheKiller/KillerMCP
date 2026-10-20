using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace KillerMCP.Setup
{
    internal static class UpdatePreferences
    {
        internal static string PathFor(string destination, bool isolated) => Path.Combine(
            isolated ? Path.GetDirectoryName(destination)! : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP"),
            "update-preferences.json");

        internal static string Read(string path)
        {
            try { return Behavior(ReadValues(path)) ?? "check"; }
            catch (IOException) { return "check"; }
            catch (UnauthorizedAccessException) { return "check"; }
        }

        internal static void Save(string path, string selected, string initial)
        {
            if (!IsValid(selected)) throw new ArgumentException("Unknown update behavior.", nameof(selected));
            var values = ReadValues(path);
            // An untouched dropdown must preserve changes made by the running notifier.
            if (selected == initial && Behavior(values) != null) return;
            values["Behavior"] = selected;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(values) + Environment.NewLine);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static Dictionary<string, object> ReadValues(string path)
        {
            try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) ?? new Dictionary<string, object>(); }
            catch (FileNotFoundException) { return new Dictionary<string, object>(); }
            catch (DirectoryNotFoundException) { return new Dictionary<string, object>(); }
            catch (ArgumentException) { return new Dictionary<string, object>(); }
            // Valid JSON that is not an object, such as an array, is treated like a missing file.
            catch (InvalidOperationException) { return new Dictionary<string, object>(); }
        }

        private static string? Behavior(Dictionary<string, object> values) =>
            values.TryGetValue("Behavior", out object value) && value is string text && IsValid(text.ToLowerInvariant())
                ? text.ToLowerInvariant() : null;

        private static bool IsValid(string value) => value is "off" or "check" or "download" or "install";
    }
}
