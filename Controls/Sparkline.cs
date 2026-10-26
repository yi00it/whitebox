using System.Windows;
using System.Windows.Media;

namespace Whitebox.Controls;

/// <summary>Lightweight area chart for live metrics. Assign a new array to redraw.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[]? Values { get => (double[]?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    // Faint dashed guides in the line colour, so they follow the theme.
    private Pen CreateGridPen()
    {
        var c = (Stroke as SolidColorBrush)?.Color ?? Colors.Gray;
        var p = new Pen(new SolidColorBrush(Color.FromArgb(0x30, c.R, c.G, c.B)), 1) { DashStyle = new DashStyle([3, 4], 0) };
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var gridPen = CreateGridPen();
        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(h * i / 4) + 0.5;
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        var values = Values;
        if (values == null || values.Length < 2) return;

        double max = Maximum > 0 ? Maximum : Math.Max(values.Max() * 1.2, 1);
        double step = w / (values.Length - 1);
        double inset = 2;

        Point P(int i) => new(i * step, inset + (h - inset * 2) * (1 - Math.Clamp(values[i] / max, 0, 1)));

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(P(0), false, false);
            for (int i = 1; i < values.Length; i++) ctx.LineTo(P(i), true, true);
        }
        line.Freeze();

        if (Fill != null)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                for (int i = 0; i < values.Length; i++) ctx.LineTo(P(i), false, true);
                ctx.LineTo(new Point(w, h), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(Fill, null, area);
        }

        var pen = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, line);

        var last = P(values.Length - 1);
        dc.DrawEllipse(Fill ?? Stroke, new Pen(Stroke, 2), last, 4, 4);
    }
}
