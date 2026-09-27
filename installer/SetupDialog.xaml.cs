using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KillerMCP.Setup
{
    public partial class SetupDialog : Window
    {
        private bool _confirmed;

        private SetupDialog(string heading, string detail, bool confirmation)
        {
            InitializeComponent();
            HeadingText.Text = heading;
            DetailText.Text = detail;
            CancelButton.Visibility = confirmation ? Visibility.Visible : Visibility.Collapsed;
            OkButton.Content = confirmation ? "Uninstall" : "OK";
            NoticeGlyph.Text = confirmation ? "!" : "×";
            NoticeGlyph.Foreground = new SolidColorBrush(confirmation
                ? Color.FromRgb(255, 190, 80)
                : Color.FromRgb(227, 93, 106));
            NoticeRing.BorderBrush = NoticeGlyph.Foreground;
            GrainLayer.Background = CreateGrain();
        }

        internal static bool ConfirmUninstall(Window? owner = null)
        {
            bool ownsApplication = Application.Current == null;
            Application? application = ownsApplication ? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown } : null;
            var dialog = new SetupDialog(
                "Uninstall KillerMCP?",
                "This removes KillerMCP and disconnects it from compatible agent clients on this computer.",
                confirmation: true);
            if (owner != null) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            dialog.ShowDialog();
            bool result = dialog._confirmed;
            application?.Shutdown();
            return result;
        }

        internal static void ShowFailure(string message)
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var dialog = new SetupDialog("KillerMCP setup could not continue", message, confirmation: false);
            dialog.ShowDialog();
            application.Shutdown();
        }

        private void Ok_Click(object sender, RoutedEventArgs e) { _confirmed = true; Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { _confirmed = false; Close(); }
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
