using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

namespace KillerMCP.Notify;

/// <summary>
/// What the repair approval window shows. The title and effects are fixed per repair, while the target and
/// the command carry what the agent chose, so a request missing any of them is refused.
/// </summary>
internal sealed class RepairApprovalText
{
    private RepairApprovalText(string action, string title, string tier, string effects, string target, string command)
    {
        Action = action; Title = title; Tier = tier; Effects = effects; Target = target; Command = command;
    }

    public string Action { get; }
    public string Title { get; }
    public string Tier { get; }
    public string Effects { get; }
    public string Target { get; }
    public string Command { get; }

    public static RepairApprovalText? Parse(string encoded)
    {
        try
        {
            var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            if (values is null) return null;
            var text = new RepairApprovalText(Text(values, "Action"), Text(values, "Title"), Text(values, "Tier"), Text(values, "Effects"),
                Text(values, "Target"), Text(values, "Command"));
            if (string.IsNullOrWhiteSpace(text.Title) || string.IsNullOrWhiteSpace(text.Command) || string.IsNullOrWhiteSpace(text.Target)
                || text.Tier is not ("Safe" or "Disruptive")) return null;
            return text;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // The host sends its confirmation record with property names as written, so a missing or non-text field reads as empty.
    private static string Text(Dictionary<string, object> values, string name) =>
        values.TryGetValue(name, out object? value) && value is string text ? text : string.Empty;
}
