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

        private SetupDialog(string heading, string detail, bool confirmation, string? confirmLabel = null, bool cancelIsDefault = false)
        {
            InitializeComponent();
            TaskbarIdentity.Track(this);
            HeadingText.Text = heading;
            DetailText.Text = detail;
            CancelButton.Visibility = confirmation ? Visibility.Visible : Visibility.Collapsed;
            OkButton.Content = confirmLabel ?? (confirmation ? "Uninstall" : "OK");
            // For a step that cannot be undone, a stray Enter cancels instead of confirming.
            OkButton.IsDefault = !cancelIsDefault;
            CancelButton.IsDefault = cancelIsDefault;
            if (cancelIsDefault) Loaded += (_, _) => CancelButton.Focus();
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
                confirmation: true,
                cancelIsDefault: true);
            if (owner != null) { dialog.Owner = owner; dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            dialog.ShowDialog();
            bool result = dialog._confirmed;
            application?.Shutdown();
            return result;
        }

        internal static bool ConfirmUpdate(string version)
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var dialog = new SetupDialog(
                "KillerMCP " + version + " is ready",
                "A signed update is available. Install it now to refresh the local MCP host and agent connections.",
                confirmation: true,
                confirmLabel: "Update");
            dialog.ShowDialog();
            bool result = dialog._confirmed;
            application.Shutdown();
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
