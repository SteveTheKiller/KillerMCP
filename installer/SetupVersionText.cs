using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerMCP.Setup
{
    // The installer sidebar version: a muted label line and a centered version line, kept in the
    // order the localized format string gives them. KillerPDF is the reference. Pass an open action
    // to make the version a link, for example to the release notes.
    public sealed class SetupVersionText : Decorator
    {
        private readonly StackPanel _lines = new StackPanel();
        private readonly TextBlock _label = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center };
        private readonly TextBlock _number = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center };

        private string _numberText = string.Empty;

        public SetupVersionText() => Child = _lines;

        internal void SetVersion(string format, string version, Action? open = null, string? openToolTip = null)
        {
            int placeholder = format.IndexOf("{0}", StringComparison.Ordinal);
            bool numberFirst = placeholder >= 0 && string.IsNullOrWhiteSpace(format.Substring(0, placeholder));
            _label.Text = string.Format(CultureInfo.CurrentCulture, format, string.Empty)
                .Trim().Trim('\u2022').Trim();
            _numberText = version;
            _number.Inlines.Clear();
            if (open == null) _number.Text = version;
            else _number.Inlines.Add(VersionLink(version, open, openToolTip));
            _lines.Children.Clear();
            TextBlock first = numberFirst ? _number : _label;
            TextBlock second = numberFirst ? _label : _number;
            first.Margin = new Thickness(0, 0, 0, 3);
            second.Margin = new Thickness(0);
            _lines.Children.Add(first);
            _lines.Children.Add(second);
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            double normalSize = TextElement.GetFontSize(this);
            FitLine(_label, _label.Text, normalSize, constraint.Width);
            FitLine(_number, _numberText, normalSize * (12.5 / 10.5), constraint.Width);
            _lines.Measure(constraint);
            return _lines.DesiredSize;
        }

        private Hyperlink VersionLink(string version, Action open, string? toolTip)
        {
            // The link keeps the line's own color and shows an underline only on hover or focus.
            var link = new Hyperlink(new Run(version)) { TextDecorations = null, Cursor = Cursors.Hand, ToolTip = toolTip };
            link.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(TextBlock.Foreground)) { Source = _number });
            link.Click += (_, _) => open();
            void Underline(bool on) => link.TextDecorations = on || link.IsKeyboardFocused ? TextDecorations.Underline : null;
            link.MouseEnter += (_, _) => Underline(true);
            link.MouseLeave += (_, _) => Underline(false);
            link.GotKeyboardFocus += (_, _) => Underline(true);
            link.LostKeyboardFocus += (_, _) => Underline(link.IsMouseOver);
            if (toolTip != null) System.Windows.Automation.AutomationProperties.SetName(link, toolTip);
            return link;
        }

        private void FitLine(TextBlock line, string text, double normalSize, double width)
        {
            line.FontFamily = TextElement.GetFontFamily(this);
            line.FontSize = normalSize;
            var measured = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection,
                new Typeface(line.FontFamily, line.FontStyle, line.FontWeight, line.FontStretch),
                normalSize, line.Foreground, null, TextOptions.GetTextFormattingMode(this),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            // Only reduce a standalone line when even that line cannot fit at normal size.
            if (width > 0 && measured.WidthIncludingTrailingWhitespace > width)
                line.FontSize *= width / measured.WidthIncludingTrailingWhitespace;
        }
    }
}
