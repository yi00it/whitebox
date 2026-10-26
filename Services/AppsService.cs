using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using Microsoft.Win32;
using Whitebox.Core;

namespace Whitebox.Services;

public sealed class InstalledApp : ObservableObject
{
    public required string Name { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public long Size { get; init; }
    public DateTime? InstallDate { get; init; }
    public required string UninstallString { get; init; }
    public string? InstallLocation { get; init; }
    public string? IconPath { get; init; }
    public bool IsSystemWide { get; init; }

    public string SizeText => Size > 0 ? Fmt.Bytes(Size) : "—";
    public string DateText => InstallDate?.ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? "—";
    public string Subtitle => string.Join("  ·  ", new[] { Publisher, Version }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
}

public static class AppsService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledApp> GetInstalled()
    {
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        Read(apps, RegistryHive.LocalMachine, RegistryView.Registry64);
        Read(apps, RegistryHive.LocalMachine, RegistryView.Registry32);
        Read(apps, RegistryHive.CurrentUser, RegistryView.Default);
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void Read(Dictionary<string, InstalledApp> apps, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(UninstallKey);
            if (key == null) return;
            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var k = key.OpenSubKey(sub);
                    if (k == null) continue;
                    var name = (k.GetValue("DisplayName") as string)?.Trim();
                    var uninstall = k.GetValue("UninstallString") as string;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(uninstall)) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (k.GetValue("ParentKeyName") != null) continue;
                    var release = k.GetValue("ReleaseType") as string;
                    if (release is "Update" or "Hotfix" or "Security Update") continue;

                    var version = k.GetValue("DisplayVersion") as string;
                    var id = name + "|" + version;
                    if (apps.ContainsKey(id)) continue;

                    long size = k.GetValue("EstimatedSize") is int kb ? kb * 1024L : 0;
                    DateTime? date = null;
                    if (k.GetValue("InstallDate") is string d &&
                        DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        date = parsed;

                    var location = (k.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                    var icon = IconLoader.ExtractPath(k.GetValue("DisplayIcon") as string) ?? IconLoader.ExtractPath(uninstall);

                    apps[id] = new InstalledApp
                    {
                        Name = name,
                        Publisher = (k.GetValue("Publisher") as string)?.Trim(),
                        Version = version,
                        Size = size,
                        InstallDate = date,
                        UninstallString = uninstall,
                        InstallLocation = string.IsNullOrWhiteSpace(location) ? null : location,
                        IconPath = icon,
                        IsSystemWide = hive == RegistryHive.LocalMachine,
                    };
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>Launches the app's own uninstaller and waits for it to exit.</summary>
    public static async Task UninstallAsync(InstalledApp app)
    {
        var (exe, args) = SplitCommand(app.UninstallString);
        if (exe.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
            args = System.Text.RegularExpressions.Regex.Replace(args, @"(?i)/I(?=\{)", "/X");

        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true };
        using var p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
    }

    public static bool StillInstalled(InstalledApp app) =>
        GetInstalled().Any(a => a.Name == app.Name && a.Version == app.Version);

    private static (string Exe, string Args) SplitCommand(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            if (end > 0) return (cmd[1..end], cmd[(end + 1)..].Trim());
        }
        int idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) return (cmd[..(idx + 4)], cmd[(idx + 4)..].Trim());
        int space = cmd.IndexOf(' ');
        return space < 0 ? (cmd, "") : (cmd[..space], cmd[(space + 1)..]);
    }
}
