using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerMCP.Notify;

public partial class UpdateWindow : Window
{
    private readonly string _installedVersion;
    private readonly string _latestVersion;
    private readonly string _installerUrl;
    private readonly string _releaseUrl;
    private readonly string _behavior;
    private readonly bool _updated;
    private bool _loadingPreferences = true;
    private string? _downloadedInstaller;

    public UpdateWindow()
    {
        InitializeComponent();
        GrainLayer.Background = CreateGrain();
        string[] args = Environment.GetCommandLineArgs();
        _installedVersion = Value(args, "--installed") ?? "unknown";
        _latestVersion = Value(args, "--latest") ?? Value(args, "--version") ?? "unknown";
        _installerUrl = Value(args, "--installer") ?? "https://github.com/SteveTheKiller/KillerMCP/releases/latest/download/KillerMCP-Setup.exe";
        _releaseUrl = Value(args, "--release") ?? "https://github.com/SteveTheKiller/KillerMCP/releases/latest";
        _behavior = Value(args, "--behavior") ?? "check";
        _updated = Has(args, "--updated");
        SelectBehavior(ReadBehavior());
        _loadingPreferences = false;
        VersionText.Text = $"v{_installedVersion}  →  v{_latestVersion}";
        StatusText.Text = "Signed Windows installer from GitHub Releases";
        if (_updated) ShowSuccess();
        else if (_behavior is "download" or "install") Visibility = Visibility.Hidden;
        Loaded += Window_Loaded;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_updated || _behavior == "check") return;
        try
        {
            _downloadedInstaller = await UpdateInstaller.DownloadAndVerifyAsync(_latestVersion, _installerUrl);
            if (_behavior == "install")
            {
                Process.Start(new ProcessStartInfo(_downloadedInstaller, "/silent /notify") { UseShellExecute = true });
                Close();
                return;
            }
            HeadingText.Text = "Your update is ready";
            BodyText.Text = "The signed installer has been downloaded and verified. Install it when you are ready.";
            StatusText.Text = "SHA256 and Windows signature verified";
            UpdateButton.Content = "Install now";
            Visibility = Visibility.Visible;
            Activate();
        }
        catch (Exception exception)
        {
            HeadingText.Text = "The update needs your attention";
            BodyText.Text = "KillerMCP could not download and verify the update automatically.";
            StatusText.Text = exception.Message;
            UpdateButton.Content = "Open release";
            Visibility = Visibility.Visible;
            Activate();
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_updated) { Close(); return; }
        UpdateButton.IsEnabled = false;
        try
        {
            if (_downloadedInstaller is null)
            {
                StatusText.Text = "Downloading and verifying the signed update...";
                _downloadedInstaller = await UpdateInstaller.DownloadAndVerifyAsync(_latestVersion, _installerUrl);
            }
            StatusText.Text = "Starting the verified upgrade...";
            Process.Start(new ProcessStartInfo(_downloadedInstaller, "/silent /notify") { UseShellExecute = true });
            Close();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Automatic update failed. Opening the release page.";
            UpdateButton.IsEnabled = true;
            Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            Debug.WriteLine(exception);
        }
    }

    private void ShowSuccess()
    {
        HeadingText.Text = "KillerMCP is up to date";
        VersionText.Text = $"Updated to v{_latestVersion}";
        BodyText.Text = "The update finished successfully. Open a new agent chat to use the newest Killer tools.";
        StatusText.Text = "Update installed and client connections refreshed";
        UpdateButton.Visibility = Visibility.Collapsed;
        LaterButton.Content = "Close";
        LaterButton.Margin = new Thickness(0);
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private static bool Has(string[] args, string name) => args.Any(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? Value(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static ImageBrush CreateGrain()
    {
        const int size = 128;
        byte[] pixels = new byte[size * size * 4];
        var random = new Random(1984);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte value = (byte)random.Next(70, 170);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = (byte)random.Next(26, 80);
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
