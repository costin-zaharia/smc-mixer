using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SmcMixer.App;

/// <summary>A read-only vertical fader that mirrors a 0..1 value.</summary>
public sealed class FaderControl : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(FaderControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double ThumbHeight = 38;

    private static readonly Brush Track = Frozen(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)));
    private static readonly Brush Filled = Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xFF)));
    private static readonly Brush Thumb = Frozen(new LinearGradientBrush(
        Color.FromRgb(0x6A, 0x6A, 0x72), Color.FromRgb(0x2E, 0x2E, 0x34), 90));
    private static readonly Pen ThumbEdge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92)), 1));
    private static readonly Pen Mark = Frozen(new Pen(Brushes.White, 2));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        double travel = h - ThumbHeight;
        double top = (1 - Math.Clamp(Value, 0, 1)) * travel;
        double centerY = top + ThumbHeight / 2;

        double trackTop = ThumbHeight / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(w / 2 - 3, trackTop, 6, travel), 3, 3);
        dc.DrawRoundedRectangle(Filled, null, new Rect(w / 2 - 3, centerY, 6, trackTop + travel - centerY), 3, 3);

        dc.DrawRoundedRectangle(Thumb, ThumbEdge, new Rect(4, top, w - 8, ThumbHeight), 5, 5);
        dc.DrawLine(Mark, new Point(10, centerY), new Point(w - 10, centerY));
    }

    private static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}

/// <summary>An endless knob; Angle is in degrees and just rotates the indicator.</summary>
public sealed class KnobControl : FrameworkElement
{
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(KnobControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Body = Frozen(new RadialGradientBrush(
        Color.FromRgb(0x4A, 0x4A, 0x52), Color.FromRgb(0x1C, 0x1C, 0x20)));
    private static readonly Pen Edge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x72)), 1.5));
    private static readonly Pen Pointer = Frozen(new Pen(Brushes.White, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    public double Angle
    {
        get => (double)GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 2;

        dc.DrawEllipse(Body, Edge, center, radius, radius);

        dc.PushTransform(new RotateTransform(Angle, center.X, center.Y));
        dc.DrawLine(Pointer, new Point(center.X, center.Y - radius * 0.35), new Point(center.X, center.Y - radius * 0.85));
        dc.Pop();
    }

    private static T Frozen<T>(T value) where T : Freezable
    {
        value.Freeze();
        return value;
    }
}
