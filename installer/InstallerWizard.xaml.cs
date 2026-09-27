using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerMCP.Setup
{
    public partial class InstallerWizard : Window
    {
        private readonly string _destination;
        private readonly bool _isolated;
        private bool _installed;
        private string? _installedVersion;

        private InstallerWizard(string destination, bool isolated)
        {
            _destination = destination;
            _isolated = isolated;
            InitializeComponent();
            VersionLabel.Text = "SETUP " + Program.CurrentVersion;
            InstallLocation.Text = destination;
            ImageBrush grain = CreateGrain();
            GrainLayer.Background = grain;
            SidebarGrain.Background = grain;
            FrameGrain.Background = grain;
            SetRuntimeStatus();
            SetInstallState();
        }

        internal static int Run(string destination, bool isolated)
        {
            var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var wizard = new InstallerWizard(destination, isolated);
            bool? result = wizard.ShowDialog();
            application.Shutdown();
            return result == true ? 0 : 1;
        }

        private void SetRuntimeStatus()
        {
            bool ready = Program.HasRuntime10(_isolated);
            RuntimeStatus.Text = ready ? ".NET 10 runtime detected" : ".NET 10 runtime required before installation";
            RuntimeStatus.Foreground = new SolidColorBrush(ready ? System.Windows.Media.Color.FromRgb(30, 165, 76) : System.Windows.Media.Color.FromRgb(255, 190, 80));
            RuntimeLink.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            InstallButton.IsEnabled = ready;
        }

        private void SetInstallState()
        {
            _installedVersion = Program.InstalledVersion(_destination);
            bool present = _installedVersion != null;
            UninstallButton.Visibility = present ? Visibility.Visible : Visibility.Collapsed;
            if (!present) return;

            bool current = string.Equals(_installedVersion, Program.CurrentVersion, StringComparison.Ordinal);
            Heading.Text = current ? "KillerMCP is already installed" : "KillerMCP is ready to upgrade";
            Status.Text = "Installed version " + _installedVersion;
            InstallButton.Content = current ? "Reinstall" : "Upgrade";
        }

        private async void Install_Click(object sender, RoutedEventArgs e)
        {
            if (_installed) { DialogResult = true; return; }
            if (!Program.HasRuntime10(_isolated)) { SetRuntimeStatus(); return; }
            try
            {
                InstallButton.IsEnabled = false;
                Heading.Text = "Installing KillerMCP";
                Status.Text = "Verifying the native runtime and connecting compatible agent clients...";
                await Task.WhenAll(Task.Run(() => Program.Install(_destination)), Task.Delay(700));
                _installed = true;
                Heading.Text = "KillerMCP is ready";
                Status.Text = "Installed and connected. Open a new agent chat to use the tools.";
                Status.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 165, 76));
                InstallButton.Content = "Done";
                InstallButton.IsEnabled = true;
                UninstallButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                Status.Text = exception.Message;
                Status.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(227, 93, 106));
                InstallButton.IsEnabled = true;
            }
        }

        private async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (!SetupDialog.ConfirmUninstall(this)) return;
            try
            {
                InstallButton.IsEnabled = false;
                UninstallButton.IsEnabled = false;
                Heading.Text = "Uninstalling KillerMCP";
                Status.Text = "Removing the native runtime and agent connections...";
                await Task.WhenAll(Task.Run(() => Program.UninstallRegistered(_destination)), Task.Delay(700));
                _installed = true;
                Heading.Text = "KillerMCP was uninstalled";
                Status.Text = "KillerMCP and its agent connections were removed.";
                Status.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 165, 76));
                InstallButton.Content = "Done";
                InstallButton.IsEnabled = true;
                UninstallButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                Status.Text = exception.Message;
                Status.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(227, 93, 106));
                InstallButton.IsEnabled = true;
                UninstallButton.IsEnabled = true;
            }
        }

        private void RuntimeLink_Click(object sender, MouseButtonEventArgs e) => Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/en-us/download/dotnet/10.0") { UseShellExecute = true });
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

        private static ImageBrush CreateGrain()
        {
            const int size = 128;
            var pixels = new byte[size * size * 4];
            var random = new Random(1979);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte value = (byte)random.Next(82, 174);
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                pixels[i + 3] = (byte)random.Next(34, 92);
            }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            bitmap.Freeze();
            return new ImageBrush(bitmap)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, size, size),
                Stretch = Stretch.None
            };
        }
    }
}
