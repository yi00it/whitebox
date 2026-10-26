using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Whitebox.Controls;

/// <summary>Border with a hard, unblurred offset shadow — the "printed card" look used across the app.</summary>
public class Surface : Border
{
    public static readonly DependencyProperty ShadowOffsetProperty = DependencyProperty.Register(
        nameof(ShadowOffset), typeof(double), typeof(Surface),
        new FrameworkPropertyMetadata(5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShadowBrushProperty = DependencyProperty.Register(
        nameof(ShadowBrush), typeof(Brush), typeof(Surface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ShadowOffset { get => (double)GetValue(ShadowOffsetProperty); set => SetValue(ShadowOffsetProperty, value); }
    public Brush? ShadowBrush { get => (Brush?)GetValue(ShadowBrushProperty); set => SetValue(ShadowBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double o = ShadowOffset;
        if (ShadowBrush != null && o > 0 && ActualWidth > 0 && ActualHeight > 0)
        {
            double r = CornerRadius.TopLeft;
            dc.DrawRoundedRectangle(ShadowBrush, null, new Rect(o, o, ActualWidth, ActualHeight), r, r);
        }
        base.OnRender(dc);
    }
}
