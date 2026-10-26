using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Whitebox.Controls;

/// <summary>Animated circular progress ring. Value is a 0..1 fraction.</summary>
public sealed class RingGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge), new PropertyMetadata(0d, OnValueChanged));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        "DisplayValue", typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xE6, 0xE1, 0xF3)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OutlineProperty = DependencyProperty.Register(
        nameof(Outline), typeof(Brush), typeof(RingGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush Brush { get => (Brush)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    /// <summary>Optional ink line drawn along both edges of the ring.</summary>
    public Brush? Outline { get => (Brush?)GetValue(OutlineProperty); set => SetValue(OutlineProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var target = Math.Clamp((double)e.NewValue, 0, 1);
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ((RingGauge)d).BeginAnimation(DisplayValueProperty, anim);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        double t = Thickness;
        if (size <= t) return;

        double r = (size - t) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(TrackBrush, t), center, r, r);
        DrawArc(dc, center, r, t);

        if (Outline != null)
        {
            var edge = new Pen(Outline, 1.5);
            dc.DrawEllipse(null, edge, center, r + t / 2, r + t / 2);
            dc.DrawEllipse(null, edge, center, r - t / 2, r - t / 2);
        }
    }

    private void DrawArc(DrawingContext dc, Point center, double r, double t)
    {
        double v = (double)GetValue(DisplayValueProperty);
        if (v <= 0.002) return;

        var cap = Outline != null ? PenLineCap.Flat : PenLineCap.Round;
        var pen = new Pen(Brush, t) { StartLineCap = cap, EndLineCap = cap };
        if (v >= 0.9995)
        {
            dc.DrawEllipse(null, pen, center, r, r);
            return;
        }

        double angle = v * 360;
        double rad = (angle - 90) * Math.PI / 180;
        var start = new Point(center.X, center.Y - r);
        var end = new Point(center.X + r * Math.Cos(rad), center.Y + r * Math.Sin(rad));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}
