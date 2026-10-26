using System.Windows;
using Microsoft.Win32;

namespace Whitebox.Core;

/// <summary>Light/dark palette switching. The palette is merged dictionary #0 of the application resources.</summary>
public static class ThemeManager
{
    private const string SettingsKey = @"Software\Whitebox";
    private const string ThemeValue = "Theme";

    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    /// <summary>Picks the saved theme, or follows Windows' app theme on first run. <paramref name="forced"/> ("light"/"dark") wins.</summary>
    public static void Initialize(string? forced)
    {
        bool dark = forced?.ToLowerInvariant() switch
        {
            "dark" => true,
            "light" => false,
            _ => LoadSaved() ?? WindowsPrefersDark(),
        };
        Apply(dark);
    }

    public static void Toggle()
    {
        Apply(!IsDark);
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(SettingsKey);
            k.SetValue(ThemeValue, IsDark ? "dark" : "light");
        }
        catch { /* preference just won't stick */ }
    }

    private static void Apply(bool dark)
    {
        IsDark = dark;
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml"),
        };
        Application.Current.Resources.MergedDictionaries[0] = palette;
        Changed?.Invoke();
    }

    private static bool? LoadSaved()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return (k?.GetValue(ThemeValue) as string) switch
            {
                "dark" => true,
                "light" => false,
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool WindowsPrefersDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }
}
