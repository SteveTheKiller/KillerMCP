using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KillerMCP.Notify
{
    // The installer sidebar version: the app's version number on its own centered line under the
    // wordmark, a size larger than the sidebar text. KillerPDF is the reference. Pass an open action
    // to make the version a link, for example to the release notes.
    public sealed class SetupVersionText : Decorator
    {
        private readonly TextBlock _number = new TextBlock { TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center };

        private string _numberText = string.Empty;

        public SetupVersionText() => Child = _number;

        internal void SetVersion(string version, Action? open = null, string? openToolTip = null)
        {
            _numberText = version;
            _number.Inlines.Clear();
            if (open == null) _number.Text = version;
            else _number.Inlines.Add(VersionLink(version, open, openToolTip));
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            FitLine(_number, _numberText, TextElement.GetFontSize(this) * (12.5 / 10.5), constraint.Width);
            _number.Measure(constraint);
            return _number.DesiredSize;
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
            // Only reduce the line when it cannot fit at its normal size.
            if (width > 0 && measured.WidthIncludingTrailingWhitespace > width)
                line.FontSize *= width / measured.WidthIncludingTrailingWhitespace;
        }
    }
}
