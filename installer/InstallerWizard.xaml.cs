using System;
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
        private readonly Action _install;
        private int _page;
        private bool _installed;

        private InstallerWizard(string destination, Action install)
        {
            _destination = destination;
            _install = install;
            InitializeComponent();
            SetupVersionLabel.Text = "SETUP " + Program.CurrentVersion;
            InstallLocation.Text = destination;
            ImageBrush grain = CreateGrain();
            GrainLayer.Background = grain;
            SidebarGrain.Background = grain;
            FrameGrain.Background = grain;
            RenderPage();
        }

        internal static int Run(string destination, Action install)
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var wizard = new InstallerWizard(destination, install);
            bool? result = wizard.ShowDialog();
            application.Shutdown();
            return result == true ? 0 : 1;
        }

        private void RenderPage()
        {
            BackButton.IsEnabled = _page > 0 && !_installed;
            CancelButton.Visibility = _installed ? Visibility.Collapsed : Visibility.Visible;
            Details.Visibility = !_installed && _page == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (_page == 0)
            {
                Heading.Text = "Welcome to KillerMCP Setup";
                Copy.Text = "Install one shared connection for KillerTools and supported Killer apps. No Node installation or source checkout is required.";
                Status.Text = "Ready to review";
                NextButton.Content = "Next";
            }
            else if (!_installed)
            {
                Heading.Text = "Ready to install";
                Copy.Text = "Setup will install KillerMCP for your Windows account and connect every compatible agent client it finds.";
                Status.Text = "Version " + Program.CurrentVersion;
                NextButton.Content = "Install";
            }
            else
            {
                Heading.Text = "KillerMCP is ready";
                Copy.Text = "The shared runtime is installed. Open a new agent chat and ask naturally, such as: killer domain search thekiller.net";
                Status.Text = "Installed and connected";
                NextButton.Content = "Done";
            }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_installed) { DialogResult = true; return; }
            if (_page == 0) { _page = 1; RenderPage(); return; }
            try
            {
                NextButton.IsEnabled = BackButton.IsEnabled = CancelButton.IsEnabled = false;
                Heading.Text = "Installing KillerMCP";
                Copy.Text = "Verifying the packaged runtime and connecting compatible agent clients.";
                Details.Visibility = Visibility.Collapsed;
                Status.Visibility = Visibility.Collapsed;
                InstallProgress.Visibility = Visibility.Visible;
                await Task.WhenAll(Task.Run(_install), Task.Delay(900));
                _installed = true;
                InstallProgress.Visibility = Visibility.Collapsed;
                Status.Visibility = Visibility.Visible;
                NextButton.IsEnabled = true;
                RenderPage();
            }
            catch (Exception error)
            {
                InstallProgress.Visibility = Visibility.Collapsed;
                Status.Visibility = Visibility.Visible;
                Status.Text = error.Message;
                Status.Foreground = new SolidColorBrush(Color.FromRgb(227, 93, 106));
                Details.Visibility = Visibility.Visible;
                NextButton.IsEnabled = BackButton.IsEnabled = CancelButton.IsEnabled = true;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_page > 0) { _page--; RenderPage(); }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

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
