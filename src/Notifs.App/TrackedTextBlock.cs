using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Notifs.App;

/// <summary>Ports the reference's heading tracking without changing the font or its glyph shapes.</summary>
public sealed class TrackedTextBlock : Decorator
{
    private readonly TextBlock textView = new() { TextWrapping = TextWrapping.Wrap };
    private double adjustment;
    private string? effectText;
    private const FrameworkPropertyMetadataOptions FontOptions = FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure;
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TrackedTextBlock), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty TrackingProperty = DependencyProperty.Register(nameof(Tracking), typeof(double), typeof(TrackedTextBlock), new FrameworkPropertyMetadata(-0.3, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(TrackedTextBlock), new FrameworkPropertyMetadata(16.0, FontOptions));
    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(TrackedTextBlock), new FrameworkPropertyMetadata(FontWeights.Normal, FontOptions));
    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(TrackedTextBlock), new FrameworkPropertyMetadata(new FontFamily("Segoe UI"), FontOptions));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(TrackedTextBlock), new FrameworkPropertyMetadata(Brushes.Black, FontOptions));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double Tracking { get => (double)GetValue(TrackingProperty); set => SetValue(TrackingProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public TrackedTextBlock() { Child = textView; }
    protected override Size MeasureOverride(Size constraint)
    {
        var text = Text ?? ""; var positions = StringInfo.ParseCombiningCharacters(text);
        textView.Text = text; textView.FontFamily = FontFamily; textView.FontSize = FontSize; textView.FontWeight = FontWeight; textView.Foreground = Foreground;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection, typeface, FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var delta = Tracking * Math.Max(0, positions.Length - 1);
        // Multiline and bidirectional text retain native wrapping and shaping.
        var singleLine = FlowDirection == FlowDirection.LeftToRight && !text.Any(ch => ch is '\r' or '\n' || ch is >= '\u0590' and <= '\u08FF') &&
            formatted.WidthIncludingTrailingWhitespace + delta <= constraint.Width;
        adjustment = singleLine ? delta : 0;
        var key = singleLine ? text + "\0" + Tracking.ToString(CultureInfo.InvariantCulture) : null;
        if (effectText != key)
        {
            effectText = key;
            var effects = new TextEffectCollection();
            if (singleLine) for (var index = 1; index < positions.Length; index++)
                effects.Add(new TextEffect { PositionStart = positions[index], PositionCount = (index + 1 < positions.Length ? positions[index + 1] : text.Length) - positions[index], Transform = new TranslateTransform(index * Tracking, 0) });
            textView.TextEffects = effects;
        }
        textView.Measure(new(Math.Max(0, constraint.Width - adjustment), constraint.Height));
        return new(Math.Max(0, textView.DesiredSize.Width + adjustment), textView.DesiredSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        textView.Arrange(new Rect(0, 0, Math.Max(0, finalSize.Width - adjustment), finalSize.Height)); return finalSize;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new TextPeer(this);
    private sealed class TextPeer(TrackedTextBlock owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.Text;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override List<AutomationPeer>? GetChildrenCore() => null;
    }
}
