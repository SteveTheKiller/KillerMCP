using KillerMCP;

internal static class NotesStagingReviewChecks
{
    public static async Task RunAsync()
    {
        // A KillerNotes that does not answer its help request never gets the note on its command line.
        var silent = Path.Combine(Environment.SystemDirectory, "where.exe");
        var (arguments, folder, problem) = await NotesContent.ArgumentsAsync(silent, "private note text", CancellationToken.None);
        if (problem != NotesContent.ProbeRefusal || arguments.Length != 0 || folder is not null)
            throw new Exception("A failed KillerNotes help request still sent the note: " + string.Join(' ', arguments));

        // Staged text sits in a folder only this account can open, inside the protected settings folder.
        var fake = Path.Combine(Path.GetTempPath(), "killermcp-staging-" + Guid.NewGuid().ToString("N"), "KillerNotes.exe");
        NotesContent.Remember(fake, "--content-file <path>");
        (arguments, folder, problem) = await NotesContent.ArgumentsAsync(fake, "private note text", CancellationToken.None);
        try
        {
            if (problem is not null || folder is null || arguments.Length != 2 || arguments[0] != "--content-file") throw new Exception("Note text was not staged in a file.");
            if (!Path.GetDirectoryName(folder)!.Equals(NotesContent.StagingRoot, StringComparison.OrdinalIgnoreCase)
                || AgentPaths.ReadRefusal(arguments[1]) is null) throw new Exception("Note text was staged where an agent can read it: " + arguments[1]);
            if (OperatingSystem.IsWindows()) OwnerOnly(folder);
        }
        finally { NotesContent.Delete(folder); }
        if (folder is not null && Directory.Exists(folder)) throw new Exception("The note staging folder was left behind.");
        Console.WriteLine("PASS: failed KillerNotes probe refused, note text staged owner-only in the protected folder.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void OwnerOnly(string folder)
    {
        var acl = new DirectoryInfo(folder).GetAccessControl();
        if (!acl.AreAccessRulesProtected) throw new Exception("The note staging folder inherits access from its parent.");
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        foreach (System.Security.AccessControl.FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)))
            if (!rule.IdentityReference.Equals(user)) throw new Exception("The note staging folder grants access to " + rule.IdentityReference.Value + ".");
    }
}
