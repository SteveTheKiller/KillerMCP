using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KillerMCP.Notify
{
    // The installer sidebar version: a muted label line and a centered version line, kept in the
    // order the localized format string gives them. KillerPDF is the reference.
    public sealed class SetupVersionText : Decorator
    {
        private readonly StackPanel _lines = new StackPanel();
        private readonly TextBlock _label = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center };
        private readonly TextBlock _number = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center };

        public SetupVersionText() => Child = _lines;

        internal void SetVersion(string format, string version)
        {
            int placeholder = format.IndexOf("{0}", StringComparison.Ordinal);
            bool numberFirst = placeholder >= 0 && string.IsNullOrWhiteSpace(format.Substring(0, placeholder));
            _label.Text = string.Format(CultureInfo.CurrentCulture, format, string.Empty)
                .Trim().Trim('\u2022').Trim();
            _number.Text = version;
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
            FitLine(_label, normalSize, constraint.Width);
            FitLine(_number, normalSize * (12.5 / 10.5), constraint.Width);
            _lines.Measure(constraint);
            return _lines.DesiredSize;
        }

        private void FitLine(TextBlock line, double normalSize, double width)
        {
            line.FontFamily = TextElement.GetFontFamily(this);
            line.FontSize = normalSize;
            var measured = new FormattedText(line.Text, CultureInfo.CurrentCulture, FlowDirection,
                new Typeface(line.FontFamily, line.FontStyle, line.FontWeight, line.FontStretch),
                normalSize, line.Foreground, null, TextOptions.GetTextFormattingMode(this),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            // Only reduce a standalone line when even that line cannot fit at normal size.
            if (width > 0 && measured.WidthIncludingTrailingWhitespace > width)
                line.FontSize *= width / measured.WidthIncludingTrailingWhitespace;
        }
    }
}
