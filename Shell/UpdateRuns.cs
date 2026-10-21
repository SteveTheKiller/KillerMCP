namespace KillerMCP.Notify;

/// <summary>
/// Tracks which download owns the update window. Each download is a run; a newer run, or a newer
/// notification that supersedes the current one, takes over, and an older run that finishes later
/// neither clears the busy state nor acts on what it downloaded.
/// </summary>
internal sealed class UpdateRuns
{
    private int _current;

    /// <summary>True while the current run is downloading.</summary>
    internal bool Busy { get; private set; }

    /// <summary>Starts a run and makes it the current one.</summary>
    internal int Begin()
    {
        Busy = true;
        return ++_current;
    }

    internal bool IsCurrent(int run) => run == _current;

    /// <summary>Ends a run. Returns true when it was still the current one, so the caller restores
    /// the window; a superseded run changes nothing.</summary>
    internal bool End(int run)
    {
        if (run != _current) return false;
        Busy = false;
        return true;
    }

    /// <summary>Drops the current run, for a notification that replaces what the window shows.</summary>
    internal void Supersede()
    {
        _current++;
        Busy = false;
    }
}
