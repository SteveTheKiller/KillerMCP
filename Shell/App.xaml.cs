using System.IO;
using System.Text.Json;
using System.Windows;

namespace KillerMCP.Notify;

public partial class App : Application
{
    private const string InstanceName = @"Local\KillerMCP-UpdateWindow";
    private const string WakeName = @"Local\KillerMCP-UpdateWindow-Wake";
    private const string NoticeOpenedName = @"Local\KillerMCP-UpdateNotice-Opened";
    private Mutex? _instance;
    private EventWaitHandle? _wake;
    private RegisteredWaitHandle? _wakeWait;
    private UpdateWindow? _update;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        KillerFamily.Accessibility.AccessibleControls.Initialize();
        int index = Array.FindIndex(e.Args, value => value.Equals("--confirm-repair", StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);
        if (index < 0)
        {
            StartUpdate(e.Args);
            return;
        }

        TaskbarIdentity.UseRepair();
        RepairConfirmWindow? confirm = index + 1 < e.Args.Length ? RepairConfirmWindow.TryCreate(e.Args[index + 1]) : null;
        if (confirm is null)
        {
            Shutdown(1);
            return;
        }
        MainWindow = confirm;
        confirm.Closed += (_, _) => Shutdown(confirm.Approved ? 0 : 1);
        confirm.Show();
    }

    private void StartUpdate(string[] args)
    {
        // One update window per user session. A later launch hands its arguments to the open
        // window and brings it forward instead of opening a second copy.
        _instance = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            WriteHandoff(args);
            WakeFirstInstance();
            _instance.Dispose();
            _instance = null;
            Shutdown();
            return;
        }
        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, WakeName);
        _wakeWait = ThreadPool.RegisterWaitForSingleObject(_wake, (_, _) => Dispatcher.BeginInvoke(OnWake),
            null, Timeout.Infinite, false);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        TaskbarIdentity.UseUpdate();
        Resources["GrainTileBrush"] = UpdateWindow.CreateGrain();
        _update = new UpdateWindow(args);
        MainWindow = _update;
        _update.Closed += (_, _) => Shutdown();

        // While an update is offered it waits in the notification area, so Later and the close
        // button hide the window instead of dropping the update until the next notice.
        _tray = new TrayIcon();
        _tray.OpenRequested += () => _update?.ShowFront();
        _tray.ReleaseNotesRequested += () => _update?.OpenReleaseNotes();
        _tray.ExitRequested += () => _update?.Exit();
        _update.UpdateStateChanged += SyncTray;
        SyncTray();
        _update.Start();
        SignalNoticeOpened();
    }

    // Tells the host that opened this notice that it is up, so the host records it as shown.
    private static void SignalNoticeOpened()
    {
        try
        {
            using var opened = EventWaitHandle.OpenExisting(NoticeOpenedName);
            opened.Set();
        }
        catch { }
    }

    private void SyncTray()
    {
        if (_update is null || _tray is null) return;
        if (_update.OffersUpdate) _tray.Show(_update.TrayTip);
        else _tray.Hide();
        _update.KeepInTray = _tray.IsVisible;
    }

    private void OnWake()
    {
        string[]? args = ReadHandoff();
        if (_update is null || args is null) return;
        _update.Reload(args);
        SignalNoticeOpened();
    }

    private static void WakeFirstInstance()
    {
        // The first copy creates its wake event just after it claims the instance, so a launch in
        // that gap retries briefly rather than dropping its arguments.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var wake = EventWaitHandle.OpenExisting(WakeName);
                wake.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
            catch { return; }
        }
    }

    private static string HandoffPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP", "update-window-handoff.json");

    private static void WriteHandoff(string[] args)
    {
        try
        {
            string path = HandoffPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(args) + Environment.NewLine);
        }
        catch { }
    }

    private static string[]? ReadHandoff()
    {
        try
        {
            string path = HandoffPath();
            string[]? args = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
            File.Delete(path);
            return args;
        }
        catch { return null; }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wakeWait?.Unregister(null);
        _wake?.Dispose();
        _tray?.Dispose();
        if (_instance is not null)
        {
            try { _instance.ReleaseMutex(); } catch { }
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}
