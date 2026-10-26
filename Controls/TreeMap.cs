using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Whitebox.Core;

namespace Whitebox.Controls;

public sealed class TreeMapItem
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public long Size { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsAggregate { get; init; }
}

/// <summary>Squarified treemap with hover and click-to-drill.</summary>
public sealed class TreeMap : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IReadOnlyList<TreeMapItem>), typeof(TreeMap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TreeMap)d).Invalidate()));

    public static readonly DependencyProperty ItemCommandProperty = DependencyProperty.Register(
        nameof(ItemCommand), typeof(ICommand), typeof(TreeMap));

    public IReadOnlyList<TreeMapItem>? ItemsSource
    {
        get => (IReadOnlyList<TreeMapItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? ItemCommand { get => (ICommand?)GetValue(ItemCommandProperty); set => SetValue(ItemCommandProperty, value); }

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x7C, 0x5C, 0xFF), Color.FromRgb(0x36, 0xA9, 0xFF), Color.FromRgb(0x2F, 0xD0, 0xA0),
        Color.FromRgb(0xFF, 0x8A, 0x4C), Color.FromRgb(0xFF, 0x5C, 0x9A), Color.FromRgb(0xFF, 0xC2, 0x4B),
        Color.FromRgb(0x5B, 0x8C, 0xFF), Color.FromRgb(0xB3, 0x6B, 0xFF), Color.FromRgb(0x22, 0xD3, 0xB6),
    ];

    private readonly List<(Rect Rect, TreeMapItem Item, Brush Fill)> _layout = [];
    private Size _layoutSize;
    private bool _dirty = true;
    private int _hover = -1;
    private readonly ToolTip _tip = new();
    private readonly Typeface _bold = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly Typeface _regular = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public TreeMap()
    {
        ToolTip = _tip;
        ToolTipService.SetInitialShowDelay(this, 250);
        Cursor = Cursors.Hand;
    }

    private void Invalidate()
    {
        _dirty = true;
        _hover = -1;
        InvalidateVisual();
    }

    private void EnsureLayout()
    {
        var size = new Size(ActualWidth, ActualHeight);
        if (!_dirty && size == _layoutSize) return;
        _dirty = false;
        _layoutSize = size;
        _layout.Clear();

        var items = ItemsSource;
        if (items == null || items.Count == 0 || size.Width < 10 || size.Height < 10) return;

        var sorted = items.Where(i => i.Size > 0).OrderByDescending(i => i.Size).ToList();
        double total = sorted.Sum(i => (double)i.Size);
        if (total <= 0) return;

        var bounds = new Rect(0, 0, size.Width, size.Height);
        double scale = bounds.Width * bounds.Height / total;
        var areas = sorted.Select(i => (Item: i, Area: i.Size * scale)).ToList();
        var rects = new List<(Rect, TreeMapItem)>();
        Squarify(areas, bounds, rects);

        int index = 0;
        foreach (var (rect, item) in rects)
        {
            var c = item.IsAggregate ? Color.FromRgb(0x3A, 0x41, 0x56) : Palette[index++ % Palette.Length];
            var dark = Color.FromRgb((byte)(c.R * 0.62), (byte)(c.G * 0.62), (byte)(c.B * 0.62));
            var fill = new LinearGradientBrush(c, dark, new Point(0, 0), new Point(1, 1));
            fill.Freeze();
            _layout.Add((rect, item, fill));
        }
    }

    private static void Squarify(List<(TreeMapItem Item, double Area)> items, Rect rect, List<(Rect, TreeMapItem)> output)
    {
        int i = 0;
        while (i < items.Count && rect.Width > 0.5 && rect.Height > 0.5)
        {
            double side = Math.Min(rect.Width, rect.Height);
            double rowSum = 0, worst = double.MaxValue;
            int j = i;
            double min = double.MaxValue, max = 0;
            while (j < items.Count)
            {
                double a = items[j].Area;
                double newSum = rowSum + a;
                double newMin = Math.Min(min, a), newMax = Math.Max(max, a);
                double s2 = side * side, sum2 = newSum * newSum;
                double newWorst = Math.Max(s2 * newMax / sum2, sum2 / (s2 * newMin));
                if (j > i && newWorst > worst) break;
                rowSum = newSum; worst = newWorst; min = newMin; max = newMax;
                j++;
            }

            double thickness = rowSum / side;
            if (rect.Width >= rect.Height)
            {
                double y = rect.Y;
                for (int k = i; k < j; k++)
                {
                    double h = items[k].Area / thickness;
                    output.Add((new Rect(rect.X, y, thickness, h), items[k].Item));
                    y += h;
                }
                rect = new Rect(rect.X + thickness, rect.Y, Math.Max(0, rect.Width - thickness), rect.Height);
            }
            else
            {
                double x = rect.X;
                for (int k = i; k < j; k++)
                {
                    double w = items[k].Area / thickness;
                    output.Add((new Rect(x, rect.Y, w, thickness), items[k].Item));
                    x += w;
                }
                rect = new Rect(rect.X, rect.Y + thickness, rect.Width, Math.Max(0, rect.Height - thickness));
            }
            i = j;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureLayout();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (int i = 0; i < _layout.Count; i++)
        {
            var (rect, item, fill) = _layout[i];
            var r = Rect.Inflate(rect, -2, -2);
            if (r.Width < 1 || r.Height < 1) continue;
            bool hover = i == _hover;
            double radius = Math.Min(8, Math.Min(r.Width, r.Height) / 3);
            dc.DrawRoundedRectangle(fill, hover ? HoverPen : null, r, radius, radius);
            if (hover) dc.DrawRoundedRectangle(HoverOverlay, null, r, radius, radius);

            if (r.Width > 64 && r.Height > 36)
            {
                double maxW = r.Width - 16;
                var name = new FormattedText(item.Name, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, _bold, 12.5, Brushes.White, dpi)
                { MaxTextWidth = maxW, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(name, new Point(r.X + 8, r.Y + 7));

                var size = new FormattedText(Fmt.Bytes(item.Size), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, _regular, 11.5, SubtleText, dpi)
                { MaxTextWidth = maxW, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                if (r.Height > 46) dc.DrawText(size, new Point(r.X + 8, r.Y + 24));
            }
        }
    }

    private static readonly Pen HoverPen = Freeze(new Pen(Brushes.White, 2));
    private static readonly Brush HoverOverlay = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush SubtleText = Freeze(new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)));

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _layout.Count; i++)
            if (_layout[i].Rect.Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.GetPosition(this));
        if (hit == _hover) return;
        _hover = hit;
        if (hit >= 0)
        {
            var it = _layout[hit].Item;
            _tip.Content = $"{it.Name}\n{Fmt.Bytes(it.Size)}" + (it.IsDirectory ? "  ·  click to open" : "");
            _tip.IsOpen = false;
            _tip.IsOpen = true;
        }
        else _tip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _tip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        int hit = HitTest(e.GetPosition(this));
        if (hit < 0) return;
        var item = _layout[hit].Item;
        if (item.IsAggregate) return;
        _tip.IsOpen = false;
        if (ItemCommand?.CanExecute(item) == true) ItemCommand.Execute(item);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _dirty = true;
    }
}
