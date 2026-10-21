#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace KillerFamily.Accessibility
{
    internal static class AccessibleControls
    {
        private static bool _initialized;
        private static readonly IValueConverter TooltipName = new TooltipNameConverter();
        private static Style? _focusStyle;
        private static readonly DependencyProperty OwnedNameProperty = DependencyProperty.RegisterAttached(
            "OwnedName", typeof(BindingBase), typeof(AccessibleControls));
        private static readonly DependencyProperty TrackedTooltipProperty = DependencyProperty.RegisterAttached(
            "TrackedTooltip", typeof(bool), typeof(AccessibleControls), new PropertyMetadata(false));
        private static readonly DependencyProperty TrackedLoadProperty = DependencyProperty.RegisterAttached(
            "TrackedLoad", typeof(bool), typeof(AccessibleControls), new PropertyMetadata(false));
        private static readonly DependencyPropertyDescriptor TooltipDescriptor =
            DependencyPropertyDescriptor.FromProperty(FrameworkElement.ToolTipProperty, typeof(Control));

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            EventManager.RegisterClassHandler(typeof(Control), FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler(OnControlSizeChanged), true);
            // WPF still gives Tab focus to a hyperlink whose TextBlock is collapsed: an invisible
            // stop that a screen reader announces as the window. Focus moves on past it instead.
            EventManager.RegisterClassHandler(typeof(System.Windows.Documents.Hyperlink),
                System.Windows.Input.Keyboard.PreviewGotKeyboardFocusEvent,
                new System.Windows.Input.KeyboardFocusChangedEventHandler(OnHyperlinkPreviewGotFocus));
            // An element with no size still reports itself on screen, which Accessibility Insights
            // fails as a null bounding rectangle: a scroll bar's page button at the end of its
            // track, an arrow button a theme hides, an empty status line. FromClip has UI
            // Automation report an element with no visible area as off screen.
            foreach (var type in new[] { typeof(TextBlock), typeof(RepeatButton) })
            {
                try
                {
                    AutomationProperties.IsOffscreenBehaviorProperty.OverrideMetadata(type,
                        new FrameworkPropertyMetadata(IsOffscreenBehavior.FromClip));
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
            }
        }

        private static void OnHyperlinkPreviewGotFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs args)
        {
            if (sender is not System.Windows.Documents.Hyperlink link || !ReferenceEquals(args.NewFocus, link)) return;
            DependencyObject? host = link.Parent;
            while (host is FrameworkContentElement content) host = content.Parent;
            if (host is not UIElement element || element.IsVisible) return;
            args.Handled = true;
            var direction = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0
                ? System.Windows.Input.FocusNavigationDirection.Previous
                : System.Windows.Input.FocusNavigationDirection.Next;
            link.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => link.MoveFocus(new System.Windows.Input.TraversalRequest(direction))));
        }

        // Tab into a radio group lands on its selected option, as in Windows, rather than on the
        // option that last had focus or the first one. Only for the keyboard: a click focuses the
        // option it lands on.
        private static void OnRadioGroupPreviewGotFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs args)
        {
            if (sender is not Panel group || args.NewFocus is not RadioButton target || target.IsChecked == true) return;
            if (args.OldFocus is Visual old && group.IsAncestorOf(old)) return;
            if (System.Windows.Input.InputManager.Current.MostRecentInputDevice is not System.Windows.Input.KeyboardDevice) return;
            var selected = group.Children.OfType<RadioButton>()
                .FirstOrDefault(option => option.IsChecked == true && option.IsEnabled && option.IsVisible);
            if (selected == null) return;
            args.Handled = true;
            selected.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                new Action(() => selected.Focus()));
        }

        private static void OnControlSizeChanged(object sender, SizeChangedEventArgs args)
        {
            if (sender is not Control control) return;
            // A class Loaded handler alone does not make WPF broadcast Loaded to every child.
            // The first layout attaches an instance handler, including for later popup content.
            if (!(bool)control.GetValue(TrackedLoadProperty))
            {
                control.Loaded += OnControlLoaded;
                control.SetValue(TrackedLoadProperty, true);
            }
            Prepare(control);
        }

        private static void OnControlLoaded(object sender, RoutedEventArgs args)
        {
            if (sender is not Control control) return;
            Prepare(control);
            if (control is Window window)
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(() => AccessibilityAudit.Record(window)));
        }

        internal static void Prepare(Control control)
        {
            // The focus adorner is shown by WPF for keyboard navigation, without changing layout.
            if (control is not Window && control.Focusable && control.FocusVisualStyle == null)
                control.SetCurrentValue(Control.FocusVisualStyleProperty, FocusStyle);

            // A group of radio buttons is one Tab stop, as everywhere else in Windows, and the arrow
            // keys move between its options. Menus and flyouts, which are not hosted in the window
            // itself, keep their own keyboard handling.
            if (control is RadioButton radio && VisualTreeHelper.GetParent(radio) is Panel group &&
                PresentationSource.FromVisual(radio)?.RootVisual is Window && group.Children.Count > 1 &&
                group.ReadLocalValue(System.Windows.Input.KeyboardNavigation.TabNavigationProperty) == DependencyProperty.UnsetValue &&
                group.Children.Cast<UIElement>().All(child => child is RadioButton option && option.GroupName == radio.GroupName))
            {
                System.Windows.Input.KeyboardNavigation.SetTabNavigation(group, System.Windows.Input.KeyboardNavigationMode.Once);
                System.Windows.Input.KeyboardNavigation.SetDirectionalNavigation(group, System.Windows.Input.KeyboardNavigationMode.Cycle);
                group.PreviewGotKeyboardFocus += OnRadioGroupPreviewGotFocus;
            }

            UpdateName(control);
        }

        private static void UpdateName(Control control)
        {
            var existing = BindingOperations.GetBindingBase(control, AutomationProperties.NameProperty);
            bool owned = existing != null && ReferenceEquals(existing, control.GetValue(OwnedNameProperty));
            if (!owned && IsReadable(AutomationProperties.GetName(control))) return;
            object? content = control is ContentControl button ? button.Content :
                control is HeaderedItemsControl items ? items.Header :
                control is HeaderedContentControl header ? header.Header : null;
            if (IsReadable(ReadText(content)))
            {
                if (owned) BindingOperations.ClearBinding(control, AutomationProperties.NameProperty);
                control.ClearValue(OwnedNameProperty);
                return;
            }
            if (!(bool)control.GetValue(TrackedTooltipProperty))
            {
                TooltipDescriptor.AddValueChanged(control, OnTooltipChanged);
                control.Unloaded += OnControlUnloaded;
                control.SetValue(TrackedTooltipProperty, true);
            }
            if (!IsReadable(ReadText(control.ToolTip)))
            {
                if (owned) BindingOperations.ClearBinding(control, AutomationProperties.NameProperty);
                control.ClearValue(OwnedNameProperty);
                return;
            }

            // A rich tooltip's localized TextBlock can change without replacing the tooltip.
            TextBlock? label = FindTextBlock(control.ToolTip);
            var binding = label != null
                ? new Binding(nameof(TextBlock.Text)) { Source = label }
                : new Binding(nameof(FrameworkElement.ToolTip)) { Source = control, Converter = TooltipName };
            BindingOperations.SetBinding(control, AutomationProperties.NameProperty, binding);
            control.SetValue(OwnedNameProperty, binding);
        }

        private static TextBlock? FindTextBlock(object? value)
        {
            if (value is TextBlock block && IsReadable(block.Text)) return block;
            if (value is ContentControl content) return FindTextBlock(content.Content);
            if (value is Panel panel)
                foreach (UIElement child in panel.Children)
                {
                    TextBlock? found = FindTextBlock(child);
                    if (found != null) return found;
                }
            return null;
        }

        private static void OnTooltipChanged(object? sender, EventArgs args)
        {
            if (sender is Control control) UpdateName(control);
        }

        private static void OnControlUnloaded(object sender, RoutedEventArgs args)
        {
            if (sender is not Control control) return;
            TooltipDescriptor.RemoveValueChanged(control, OnTooltipChanged);
            control.Unloaded -= OnControlUnloaded;
            control.SetValue(TrackedTooltipProperty, false);
        }

        internal static string ReadText(object? value)
        {
            if (value is string text) return text.Trim();
            if (value is TextBlock block) return block.Text.Trim();
            if (value is ContentControl content) return ReadText(content.Content);
            if (value is Panel panel)
                foreach (UIElement child in panel.Children)
                {
                    string label = ReadText(child);
                    if (IsReadable(label)) return label;
                }
            return string.Empty;
        }

        private static bool IsReadable(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("System.Windows.", StringComparison.Ordinal))
                return false;
            foreach (char character in text)
                if (char.IsLetterOrDigit(character) &&
                    CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.PrivateUse)
                    return true;
            return false;
        }

        private static Style FocusStyle
        {
            get
            {
                if (_focusStyle != null) return _focusStyle;
                var ring = new FrameworkElementFactory(typeof(FocusRing));
                ring.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
                ring.SetValue(FocusRing.InsetProperty, 2.0);
                var template = new ControlTemplate(typeof(Control)) { VisualTree = ring };
                _focusStyle = new Style(typeof(Control));
                _focusStyle.Setters.Add(new Setter(Control.TemplateProperty, template));
                _focusStyle.Seal();
                return _focusStyle;
            }
        }

        private sealed class TooltipNameConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
                ReadText(value);

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
                Binding.DoNothing;
        }
    }

    /// <summary>
    /// The keyboard focus outline: a thin solid ring in the theme accent, rounded like the
    /// controls. When the accent is too faint against the window surfaces to read as an
    /// outline, the ring uses the text color instead, and High Contrast uses the system
    /// highlight. WPF builds a new outline each time focus moves, so it follows theme changes.
    /// </summary>
    internal sealed class FocusRing : Border
    {
        internal static readonly DependencyProperty InsetProperty = DependencyProperty.Register(
            nameof(Inset), typeof(double), typeof(FocusRing), new PropertyMetadata(0.0));

        private const double RingThickness = 2;
        private const double MinimumContrast = 3;

        public FocusRing()
        {
            BorderThickness = new Thickness(RingThickness);
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;
            Loaded += (_, _) => Apply();
        }

        internal double Inset
        {
            get => (double)GetValue(InsetProperty);
            set => SetValue(InsetProperty, value);
        }

        private void Apply()
        {
            BorderBrush = RingBrush();
            double radius = TryFindResource("ControlCornerRadius") is CornerRadius corner ? corner.TopLeft : 0;
            // An outline inside the control sits on a smaller curve, one outside it on a larger one.
            CornerRadius = new CornerRadius(radius <= 0 ? 0 : Math.Max(0, radius - Inset));
        }

        private Brush RingBrush()
        {
            if (SystemParameters.HighContrast) return SystemColors.HighlightBrush;
            Brush text = TryFindResource("TextBrush") as Brush ?? Brushes.White;
            if ((TryFindResource("PrimaryBrush") ?? TryFindResource("Fuchsia")) is SolidColorBrush accent)
            {
                var surfaces = new[] { "BackgroundBrush", "PaneBrush" }
                    .SelectMany(key => SurfaceColors(TryFindResource(key) as Brush)).ToList();
                if (surfaces.Count == 0) surfaces.Add(Color.FromRgb(10, 10, 10));
                if (surfaces.All(surface => Contrast(Over(accent.Color, surface), surface) >= MinimumContrast))
                    return accent;
            }
            return text;
        }

        private static IEnumerable<Color> SurfaceColors(Brush? brush) => brush switch
        {
            SolidColorBrush solid => new[] { solid.Color },
            GradientBrush gradient => gradient.GradientStops.Select(stop => stop.Color),
            _ => Enumerable.Empty<Color>(),
        };

        private static Color Over(Color top, Color bottom)
        {
            double alpha = top.A / 255.0;
            byte Mix(byte a, byte b) => (byte)Math.Round(a * alpha + b * (1 - alpha));
            return Color.FromRgb(Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
        }

        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double Luminance(Color color)
        {
            double Channel(byte value)
            {
                double c = value / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
    }
}
