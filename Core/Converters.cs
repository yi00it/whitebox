using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Whitebox.Core;

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        long l => Fmt.Bytes(l),
        int i => Fmt.Bytes(i),
        ulong u => Fmt.Bytes((long)u),
        double d => Fmt.Bytes((long)d),
        _ => "—"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Visible when the bound value's string form is in the comma-separated parameter list.</summary>
public sealed class StateVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value?.ToString();
        var options = (parameter as string ?? "").Split(',', StringSplitOptions.TrimEntries);
        return options.Contains(s) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when a number is non-zero; pass "zero" as parameter to invert.</summary>
public sealed class CountVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double n = value switch { int i => i, long l => l, double d => d, _ => 0 };
        bool visible = n != 0;
        if (parameter as string == "zero") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when value is non-null (and non-empty string); pass "invert" to flip.</summary>
public sealed class NullVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool has = value != null && !(value is string s && s.Length == 0);
        if (parameter as string == "invert") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return "?";
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) return char.ToUpperInvariant(ch).ToString();
        return "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Maps any string to one of a fixed set of pleasant gradients, stable per name.</summary>
public sealed class NameToBrushConverter : IValueConverter
{
    private static readonly (string, string)[] Pairs =
    [
        ("#8B6CFF", "#36C9FF"), ("#2FE0A5", "#21A0FF"), ("#FF8A4C", "#FF4F8B"), ("#FFC24B", "#FF7A45"),
        ("#5B8CFF", "#9F6BFF"), ("#FF5C9A", "#B36BFF"), ("#22D3B6", "#2F86FF"), ("#F7B733", "#FC4A1A"),
    ];

    private static readonly Brush[] Brushes = Pairs.Select(p =>
    {
        var b = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(p.Item1),
            (Color)ColorConverter.ConvertFromString(p.Item2), 45);
        b.Freeze();
        return (Brush)b;
    }).ToArray();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        uint h = 2166136261;
        foreach (var ch in s.ToLowerInvariant()) h = (h ^ ch) * 16777619;
        return Brushes[h % (uint)Brushes.Length];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Converts 0..100 to 0..1.</summary>
public sealed class PercentToFractionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? d / 100.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Multiplies a 0..1 fraction by a width given as parameter or by a second binding.</summary>
public sealed class FractionWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double f || values[1] is not double w) return 0.0;
        return Math.Max(0, Math.Min(1, f)) * w;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => [];
}
