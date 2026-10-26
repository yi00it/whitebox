using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Whitebox.Controls;

/// <summary>Page title row: gradient icon tile, title, subtitle, and right-aligned content (actions).</summary>
public sealed class PageHeader : ContentControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(PageHeader));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(PageHeader));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(PageHeader));

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public Brush? Accent { get => (Brush?)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
}

public static class UiProps
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
        "Glyph", typeof(string), typeof(UiProps), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static string? GetGlyph(DependencyObject o) => (string?)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, string? v) => o.SetValue(GlyphProperty, v);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(UiProps), new PropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);

    /// <summary>Keeps a Border's corner radius at half its height (WPF turns oversized radii into ellipses).</summary>
    public static readonly DependencyProperty PillProperty = DependencyProperty.RegisterAttached(
        "Pill", typeof(bool), typeof(UiProps), new PropertyMetadata(false, OnPillChanged));

    public static bool GetPill(DependencyObject o) => (bool)o.GetValue(PillProperty);
    public static void SetPill(DependencyObject o, bool v) => o.SetValue(PillProperty, v);

    private static void OnPillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b) return;
        b.SizeChanged -= UpdatePill;
        if (e.NewValue is true) b.SizeChanged += UpdatePill;
    }

    private static void UpdatePill(object sender, SizeChangedEventArgs e) =>
        ((Border)sender).CornerRadius = new CornerRadius(e.NewSize.Height / 2);
}
