using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Whitebox.Core;

namespace Whitebox.Services;

/// <summary>Loads shell icons on a dedicated STA thread and caches them by path.</summary>
public static class IconLoader
{
    private static readonly BlockingCollection<(string Path, Action<ImageSource?> Callback)> Queue = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    static IconLoader()
    {
        var t = new Thread(Worker) { IsBackground = true, Name = "IconLoader" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    public static void Load(string? path, Action<ImageSource?> callback)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Cache.TryGetValue(path, out var cached))
        {
            callback(cached);
            return;
        }
        Queue.Add((path, callback));
    }

    private static void Worker()
    {
        foreach (var (path, callback) in Queue.GetConsumingEnumerable())
        {
            if (!Cache.TryGetValue(path, out var img))
            {
                img = Extract(path);
                Cache[path] = img;
            }
            try { callback(img); } catch { }
        }
    }

    private static ImageSource? Extract(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return null;
            var info = new Native.SHFILEINFO();
            var result = Native.SHGetFileInfo(path, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info),
                Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                Native.DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extracts an executable path from a command line or DisplayIcon value ("C:\x.exe",0 → C:\x.exe).</summary>
    public static string? ExtractPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var s = Environment.ExpandEnvironmentVariables(command.Trim());
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            return end > 1 ? s[1..end] : null;
        }
        foreach (var ext in new[] { ".exe", ".ico", ".dll", ".lnk", ".bat", ".cmd" })
        {
            int idx = s.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return s[..(idx + ext.Length)];
        }
        int comma = s.LastIndexOf(',');
        return comma > 0 ? s[..comma] : s;
    }
}
