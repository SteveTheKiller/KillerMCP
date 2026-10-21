using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace KillerMCP.Notify;

/// <summary>The notification-area icon that holds an offered update while its window is hidden.
/// A hidden top-level window receives the icon callback and TaskbarCreated, so the icon comes
/// back after Explorer restarts. Adapted from the KillerShot tray host.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1;   // WM_APP + 1
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const uint IconId = 1;
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const uint NifMessage = 0x01, NifIcon = 0x02, NifTip = 0x04;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private readonly IntPtr _icon;
    private string _tip = "KillerMCP";
    private bool _added;
    private bool _ownsIcon;

    /// <summary>A click on the icon or the Open update menu item.</summary>
    public event Action? OpenRequested;
    public event Action? ReleaseNotesRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        // WS_POPUP, never shown. A message-only window would miss the TaskbarCreated broadcast.
        var parameters = new HwndSourceParameters("KillerMCPUpdateTray") { WindowStyle = unchecked((int)0x80000000), Width = 0, Height = 0 };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadAppIcon();
    }

    public bool IsVisible => _added;

    public void Show(string tip)
    {
        _tip = tip.Length > 127 ? tip[..127] : tip;
        if (_added)
        {
            var data = NewData(NifTip);
            Shell_NotifyIcon(NimModify, ref data);
            return;
        }
        var add = NewData(NifMessage | NifIcon | NifTip);
        _added = Shell_NotifyIcon(NimAdd, ref add);
    }

    public void Hide()
    {
        if (!_added) return;
        var data = NewData(0);
        Shell_NotifyIcon(NimDelete, ref data);
        _added = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            int mouse = lParam.ToInt32() & 0xFFFF;
            if (mouse == WmLButtonUp) OpenRequested?.Invoke();
            else if (mouse == WmRButtonUp || mouse == WmContextMenu) ShowMenu();
            handled = true;
        }
        else if (_taskbarCreated != 0 && msg == (int)_taskbarCreated && _added)
        {
            _added = false;
            Show(_tip);
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        MenuItem Item(string header, Action click)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }
        Item("Open update", () => OpenRequested?.Invoke()).FontWeight = FontWeights.SemiBold;
        Item("Release notes", () => ReleaseNotesRequested?.Invoke());
        menu.Items.Add(new Separator());
        Item("Exit", () => ExitRequested?.Invoke());
        // The menu closes on an outside click only when this process owns the foreground.
        SetForegroundWindow(_source.Handle);
        menu.IsOpen = true;
    }

    private NotifyIconData NewData(uint flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _source.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>The exe's own icon at the notification area's size for this DPI, so the tray
    /// shows the KillerMCP artwork instead of a scaled 32px frame.</summary>
    private IntPtr LoadAppIcon()
    {
        IntPtr module = GetModuleHandle(null);
        IntPtr id = IntPtr.Zero;
        string? name = null;
        // The first icon group is the ApplicationIcon. A string name is only valid inside the callback.
        EnumResourceNames(module, (IntPtr)14, (_, _, found, _) =>
        {
            if (((ulong)found >> 16) == 0) id = found;
            else name = Marshal.PtrToStringUni(found);
            return false;
        }, IntPtr.Zero);
        int size = GetSystemMetrics(49);   // SM_CXSMICON
        // LoadImage picks the closest frame in the group for the requested size. LoadIconWithScaleDown
        // would be sharper but lives only in comctl32 version 6, which this exe does not activate.
        IntPtr icon = id != IntPtr.Zero ? LoadImage(module, id, 1, size, size, 0)
            : name is not null ? LoadImage(module, name, 1, size, size, 0) : IntPtr.Zero;
        if (icon != IntPtr.Zero) { _ownsIcon = true; return icon; }
        return LoadIcon(IntPtr.Zero, (IntPtr)32512);   // IDI_APPLICATION, shared and never destroyed
    }

    public void Dispose()
    {
        Hide();
        if (_ownsIcon) DestroyIcon(_icon);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern IntPtr LoadImage(IntPtr instance, IntPtr name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr parameter);
}
