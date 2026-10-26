namespace Whitebox.Core;

public static class Fmt
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < Units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        if (i == 0) return $"{bytes} B";
        return v >= 100 ? $"{v:0} {Units[i]}" : v >= 10 ? $"{v:0.0} {Units[i]}" : $"{v:0.00} {Units[i]}";
    }

    public static string Speed(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

    public static string Duration(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
        return $"{t.Minutes}m {t.Seconds}s";
    }

    public static string Count(int n, string singular, string? plural = null) =>
        $"{n:N0} {(n == 1 ? singular : plural ?? singular + "s")}";
}
