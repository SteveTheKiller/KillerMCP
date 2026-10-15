using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerMCP.Notify;

public partial class UpdateWindow : Window
{
    private string _installedVersion = "0.0.0";
    private string? _latestVersion;
    private string _installerUrl = string.Empty;
    private string _releaseUrl = string.Empty;
    private string _behavior = "check";
    private bool _updated;
    private bool _loadingPreferences = true;
    private bool _busy;
    private string? _downloadedInstaller;
    private string _statusPath = string.Empty;

    public UpdateWindow(string[] args)
    {
        InitializeComponent();
        TaskbarIdentity.Track(this);
        GrainLayer.Background = CreateGrain();
        PaneGrain.Background = GrainLayer.Background;
        Apply(args);
    }

    private bool Automatic => !_updated && _latestVersion is not null && _behavior is "download" or "install";

    /// <summary>Opens the window, or starts the background download first for the automatic
    /// update choices, which show the window only when it needs the user.</summary>
    internal void Start()
    {
        if (Automatic) _ = RunAutomaticAsync();
        else ShowFront();
    }

    /// <summary>Takes over the arguments of a later launch, so the newest notification wins
    /// without opening a second window.</summary>
    internal void Reload(string[] args)
    {
        if (_busy && Same(args)) return;
        Apply(args);
        Start();
    }

    private bool Same(string[] args) =>
        string.Equals(Value(args, "--latest") ?? Value(args, "--version"), _latestVersion, StringComparison.OrdinalIgnoreCase)
        && Has(args, "--updated") == _updated
        && string.Equals(Value(args, "--behavior") ?? "check", _behavior, StringComparison.OrdinalIgnoreCase);

    private void Apply(string[] args)
    {
        _installedVersion = Value(args, "--installed") ?? InstalledVersion();
        _latestVersion = Value(args, "--latest") ?? Value(args, "--version");
        _installerUrl = Value(args, "--installer") ?? "https://github.com/SteveTheKiller/KillerMCP/releases/latest/download/KillerMCP-Setup.exe";
        _releaseUrl = Value(args, "--release") ?? "https://github.com/SteveTheKiller/KillerMCP/releases/latest";
        _behavior = Value(args, "--behavior") ?? "check";
        _updated = Has(args, "--updated");
        _statusPath = Value(args, "--status") ?? ReleaseNotes.DefaultStatusPath();
        _downloadedInstaller = null;
        _loadingPreferences = true;
        SelectBehavior(ReadBehavior());
        _loadingPreferences = false;

        SetHeading("A new KillerMCP is ready");
        BodyText.Text = "Update the local host, reconnect supported agent clients, and pick up the newest Killer tools.";
        UpdateButton.Content = "Update now";
        UpdateButton.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = true;
        LaterButton.Content = "Later";
        LaterButton.Margin = new Thickness(0, 0, 6, 0);
        SetSidebarVersion("UPDATE  •  {0}", _latestVersion ?? _installedVersion);
        SetStatus("Signed Windows installer from GitHub Releases");
        ShowNotes(_latestVersion is null ? null : ReleaseNotes.Read(_statusPath, _latestVersion));
        if (_updated) ShowSuccess();
        else if (_latestVersion is null)
        {
            SetHeading("Update KillerMCP");
            BodyText.Text = "Open a new agent chat to check for a newer release.";
            StatusText.Text = "No update has been selected";
            UpdateButton.Visibility = Visibility.Collapsed;
            LaterButton.Content = "Close";
            LaterButton.Margin = new Thickness(0);
        }
    }

    private void ShowFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private async Task RunAutomaticAsync()
    {
        string? version = _latestVersion;
        if (version is null) return;
        _busy = true;
        try
        {
            string installer = await UpdateInstaller.DownloadAndVerifyAsync(version, _installerUrl);
            if (version != _latestVersion) return;
            _downloadedInstaller = installer;
            if (_behavior == "install")
            {
                Process.Start(new ProcessStartInfo(_downloadedInstaller, "/silent /notify") { UseShellExecute = true });
                Close();
                return;
            }
            SetHeading("Your update is ready");
            BodyText.Text = "The signed installer has been downloaded and verified. Install it when you are ready.";
            SetStatus("SHA256 and Windows signature verified");
            UpdateButton.Content = "Install now";
            ShowFront();
        }
        catch (Exception exception)
        {
            if (version != _latestVersion) return;
            SetHeading("The update needs your attention");
            BodyText.Text = "KillerMCP could not download and verify the update automatically.";
            SetStatus(exception.Message);
            UpdateButton.Content = "Open release";
            ShowFront();
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updated || _latestVersion is null) { Close(); return; }
        UpdateButton.IsEnabled = false;
        _busy = true;
        try
        {
            if (_downloadedInstaller is null)
            {
                SetStatus("Downloading and verifying the signed update...");
                _downloadedInstaller = await UpdateInstaller.DownloadAndVerifyAsync(_latestVersion, _installerUrl);
            }
            SetStatus("Starting the verified upgrade...");
            Process.Start(new ProcessStartInfo(_downloadedInstaller, "/silent /notify") { UseShellExecute = true });
            Close();
        }
        catch (Exception exception)
        {
            SetStatus("Automatic update failed. Opening the release page.");
            UpdateButton.IsEnabled = true;
            Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            Debug.WriteLine(exception);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowSuccess()
    {
        SetHeading("KillerMCP is up to date");
        SetSidebarVersion("UPDATED  •  {0}", _latestVersion ?? _installedVersion);
        BodyText.Text = "The update finished successfully. Open a new agent chat to use the newest Killer tools.";
        StatusText.Text = "Update installed and client connections refreshed";
        UpdateButton.Visibility = Visibility.Collapsed;
        LaterButton.Content = "Close";
        LaterButton.Margin = new Thickness(0);
    }

    /// <summary>The status line keeps the installed version in view while an update is offered,
    /// since the sidebar shows the version being offered.</summary>
    private void SetStatus(string text) => StatusText.Text =
        _updated || _latestVersion is null ? text : $"Installed {_installedVersion}  •  {text}";

    private void SetHeading(string text)
    {
        HeadingText.Inlines.Clear();
        string[] parts = text.Split("KillerMCP");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                // The heading reads in Killer Sans; the product name inside it is the wordmark,
                // Punktype and Lacquer scaled to the same capital heights as the title bar.
                HeadingText.Inlines.Add(new Run("Killer")
                {
                    FontFamily = (FontFamily)FindResource("Wordmark"),
                    FontSize = HeadingText.FontSize * 0.7112,
                });
                HeadingText.Inlines.Add(new Run("MCP")
                {
                    Foreground = (Brush)FindResource("Fuchsia"),
                    FontFamily = (FontFamily)FindResource("WordmarkAccent"),
                    FontSize = HeadingText.FontSize * 1.068,
                });
            }
            if (parts[i].Length > 0) HeadingText.Inlines.Add(new Run(parts[i]));
        }
    }

    private void BehaviorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingPreferences || BehaviorBox.SelectedItem is not ComboBoxItem item || item.Tag is not string behavior) return;
        WriteBehavior(behavior);
    }

    private void SelectBehavior(string behavior)
    {
        foreach (object item in BehaviorBox.Items)
        {
            if (item is ComboBoxItem option && string.Equals(option.Tag as string, behavior, StringComparison.OrdinalIgnoreCase))
            {
                BehaviorBox.SelectedItem = option;
                return;
            }
        }
        BehaviorBox.SelectedIndex = 1;
    }

    private static string ReadBehavior()
    {
        try
        {
            var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath()));
            if (preferences?.Behavior is "off" or "check" or "download" or "install") return preferences.Behavior;
        }
        catch { }
        return "check";
    }

    private static void WriteBehavior(string behavior)
    {
        string path = PreferencesPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new Preferences(behavior)) + Environment.NewLine);
    }

    private static string PreferencesPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillerMCP", "update-preferences.json");

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private void SetSidebarVersion(string format, string version) =>
        SidebarVersion.SetVersion(format, version, () => OpenUrl(ReleaseNotes.ReleaseUrl(version)), $"Open the {version} release notes");

    private void ReleaseNotes_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(ReleaseNotes.ReleaseUrl(_latestVersion ?? _installedVersion));

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }

    /// <summary>Lists the release notes between the body text and the update choice, or hides the
    /// section when the host has not stored notes for this version.</summary>
    private void ShowNotes(ReleaseNotes? notes)
    {
        NotesList.Children.Clear();
        NotesScroller.ScrollToTop();
        bool show = notes is not null;
        NotesPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        NotesRow.Height = show ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        // The notes get the extra height; without them the window keeps the family setup size.
        Height = show ? 450 : 390;
        if (notes is null) return;
        NotesHeading.Text = $"What's new in {_latestVersion}";
        NotesLink.ToolTip = $"Open the {_latestVersion} release notes on GitHub";
        System.Windows.Automation.AutomationProperties.SetName(NotesLink, "Full release notes");
        var text = new SolidColorBrush(Color.FromRgb(0xd0, 0xd0, 0xd0));
        var muted = new SolidColorBrush(Color.FromRgb(0x92, 0x92, 0x92));
        var accent = (Brush)FindResource("Fuchsia");
        if (notes.Summary.Length > 0)
            NotesList.Children.Add(new TextBlock { Text = notes.Summary, Foreground = text, TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(0, 0, 0, 4) });
        foreach (ReleaseNotes.Section section in notes.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
                NotesList.Children.Add(new TextBlock { Text = section.Heading.ToUpperInvariant(), FontSize = 9.5, Foreground = muted, Margin = new Thickness(0, 4, 0, 2) });
            foreach (string item in section.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = "•", Foreground = accent });
                var line = new TextBlock { Text = item, Foreground = text, TextWrapping = TextWrapping.Wrap, LineHeight = 16 };
                Grid.SetColumn(line, 1);
                row.Children.Add(line);
                NotesList.Children.Add(row);
            }
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private static bool Has(string[] args, string name) => args.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string InstalledVersion()
    {
        string hostPath = Path.Combine(AppContext.BaseDirectory, "KillerMCP.exe");
        string path = File.Exists(hostPath) ? hostPath : Environment.ProcessPath!;
        return FileVersionInfo.GetVersionInfo(path).ProductVersion?.Split('+')[0] ?? "0.0.0";
    }

    private static string? Value(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    internal static ImageBrush CreateGrain()
    {
        // The family film grain from KillerUI GrainTexture: about a third of the pixels carry a speck,
        // half bright and half dark, so black surfaces stay black instead of lifting toward gray.
        const int size = 256;
        var pixels = new byte[size * size * 4];
        var random = new Random(1337);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (random.Next(3) != 0) continue;
            bool bright = random.Next(2) == 0;
            byte value = bright ? (byte)random.Next(190, 255) : (byte)random.Next(0, 50);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = (byte)random.Next(35, 95);
        }
        BitmapSource bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, size, size),
            Stretch = Stretch.None
        };
    }

    private sealed record Preferences(string Behavior);
}
