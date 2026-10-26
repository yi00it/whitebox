using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using Whitebox.Core;

namespace Whitebox.Services;

public enum JunkKind { Files, RecycleBin, DnsCache, Clipboard, RunHistory }
public enum ScanState { Pending, Scanning, Scanned, Cleaning, Cleaned, Skipped }

public sealed record JunkRule(string Path, string Pattern = "*", bool Recursive = true, TimeSpan? MinAge = null);

public readonly record struct FoundFile(string Path, long Size);

public sealed class JunkCategory : ObservableObject
{
    public JunkCategory(string id, string group, string name, string description, string icon, JunkKind kind,
        bool defaultChecked, bool requiresAdmin, JunkRule[] rules, string[]? processes = null)
    {
        Id = id; Group = group; Name = name; Description = description; Icon = icon; Kind = kind;
        RequiresAdmin = requiresAdmin; Rules = rules; Processes = processes ?? [];
        _isChecked = defaultChecked && IsAvailable;
    }

    public string Id { get; }
    public string Group { get; }
    public string Name { get; }
    public string Description { get; }
    public string Icon { get; }
    public JunkKind Kind { get; }
    public bool RequiresAdmin { get; }
    public JunkRule[] Rules { get; }
    public string[] Processes { get; }

    public bool IsAvailable => !RequiresAdmin || AdminHelper.IsAdmin;
    public bool NeedsElevation => !IsAvailable;

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value && IsAvailable); }

    private long _size;
    public long Size { get => _size; set { if (Set(ref _size, value)) OnPropertyChanged(nameof(StatusText)); } }

    private int _count;
    public int Count { get => _count; set { if (Set(ref _count, value)) OnPropertyChanged(nameof(StatusText)); } }

    private ScanState _state;
    public ScanState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public bool IsBusy => State is ScanState.Scanning or ScanState.Cleaning;

    private bool _isAppRunning;
    public bool IsAppRunning { get => _isAppRunning; set => Set(ref _isAppRunning, value); }

    public string StatusText => State switch
    {
        ScanState.Pending => "—",
        ScanState.Scanning => "Scanning…",
        ScanState.Cleaning => "Cleaning…",
        ScanState.Skipped => "Skipped",
        _ when Kind == JunkKind.DnsCache => State == ScanState.Cleaned ? "Flushed" : "Ready",
        _ when Count == 0 => State == ScanState.Cleaned ? "Cleaned" : "Clean",
        _ when Size == 0 => Fmt.Count(Count, "item"),
        _ => Fmt.Bytes(Size),
    };

    internal List<FoundFile> Files { get; set; } = [];

    public IReadOnlyList<FoundFile> Preview => Files.OrderByDescending(f => f.Size).Take(400).ToList();
}

public static class JunkEngine
{
    private const string RunMruKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";
    private static readonly TimeSpan TempGrace = TimeSpan.FromHours(2);

    private static JunkRule R(string path, string pattern = "*", bool recursive = true, TimeSpan? minAge = null) =>
        new(path, pattern, recursive, minAge);

    private static JunkRule[] Chromium(string root) =>
    [
        R($@"{root}\*\Cache"), R($@"{root}\*\Code Cache"), R($@"{root}\*\GPUCache"),
        R($@"{root}\*\DawnCache"), R($@"{root}\*\DawnWebGPUCache"), R($@"{root}\*\DawnGraphiteCache"),
        R($@"{root}\*\Service Worker\CacheStorage"), R($@"{root}\*\Service Worker\ScriptCache"),
        R($@"{root}\ShaderCache"), R($@"{root}\GrShaderCache"), R($@"{root}\GraphiteDawnCache"),
    ];

    private static JunkRule[] Electron(string root, params JunkRule[] extra) =>
    [
        R($@"{root}\Cache"), R($@"{root}\Code Cache"), R($@"{root}\GPUCache"), R($@"{root}\DawnCache"),
        R($@"{root}\DawnWebGPUCache"), R($@"{root}\Service Worker\CacheStorage"), R($@"{root}\Crashpad\reports"),
        .. extra,
    ];

    public static List<JunkCategory> CreateCategories() =>
    [
        // ---------- System ----------
        new("user-temp", "System", "Temporary files", "Leftovers from apps and installers in your temp folder",
            "", JunkKind.Files, true, false, [R("%TEMP%", minAge: TempGrace)]),
        new("win-temp", "System", "Windows temporary files", "Temporary files created by Windows and services",
            "", JunkKind.Files, true, true, [R(@"%WINDIR%\Temp", minAge: TempGrace)]),
        new("thumbs", "System", "Thumbnail cache", "Explorer thumbnail and icon caches, rebuilt automatically",
            "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "thumbcache_*.db", false),
             R(@"%LOCALAPPDATA%\Microsoft\Windows\Explorer", "iconcache_*.db", false)]),
        new("wer", "System", "Error reports", "Windows Error Reporting archives and queued reports",
            "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\Microsoft\Windows\WER"), R(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportArchive"),
             R(@"%PROGRAMDATA%\Microsoft\Windows\WER\ReportQueue")]),
        new("dumps", "System", "Crash dumps", "Memory dumps written when apps or Windows crashed",
            "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\CrashDumps"), R(@"%WINDIR%\Minidump"), R("%WINDIR%", "MEMORY.DMP", false)]),
        new("wu", "System", "Windows Update cache", "Update packages that have already been installed",
            "", JunkKind.Files, true, true, [R(@"%WINDIR%\SoftwareDistribution\Download")]),
        new("do", "System", "Delivery Optimization", "Update files cached for sharing with other PCs",
            "", JunkKind.Files, true, true,
            [R(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")]),
        new("logs", "System", "System logs", "Servicing and setup log files",
            "", JunkKind.Files, true, true,
            [R(@"%WINDIR%\Logs\CBS", "*.log"), R(@"%WINDIR%\Logs\DISM", "*.log"), R(@"%WINDIR%\Logs\MoSetup", "*.log")]),
        new("inet", "System", "Internet cache", "WinINet cache used by Windows apps and WebView",
            "", JunkKind.Files, true, false, [R(@"%LOCALAPPDATA%\Microsoft\Windows\INetCache")]),
        new("shader", "System", "Graphics shader cache", "DirectX, NVIDIA and AMD shader caches (rebuilt on demand)",
            "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\D3DSCache"), R(@"%LOCALAPPDATA%\NVIDIA\DXCache"), R(@"%LOCALAPPDATA%\NVIDIA\GLCache"),
             R(@"%LOCALAPPDATA%\AMD\DxCache"), R(@"%LOCALAPPDATA%\AMD\DxcCache"), R(@"%LOCALAPPDATA%\AMD\GLCache")]),
        new("recycle", "System", "Recycle Bin", "Files you deleted that are still taking up space",
            "", JunkKind.RecycleBin, true, false, []),

        // ---------- Browsers (cache only — history, cookies and passwords are never touched) ----------
        new("chrome", "Browsers", "Google Chrome", "Web cache, code cache and GPU shader cache",
            "", JunkKind.Files, true, false, Chromium(@"%LOCALAPPDATA%\Google\Chrome\User Data"), ["chrome"]),
        new("edge", "Browsers", "Microsoft Edge", "Web cache, code cache and GPU shader cache",
            "", JunkKind.Files, true, false, Chromium(@"%LOCALAPPDATA%\Microsoft\Edge\User Data"), ["msedge"]),
        new("brave", "Browsers", "Brave", "Web cache, code cache and GPU shader cache",
            "", JunkKind.Files, true, false, Chromium(@"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data"), ["brave"]),
        new("vivaldi", "Browsers", "Vivaldi", "Web cache, code cache and GPU shader cache",
            "", JunkKind.Files, true, false, Chromium(@"%LOCALAPPDATA%\Vivaldi\User Data"), ["vivaldi"]),
        new("opera", "Browsers", "Opera", "Web cache and GPU shader cache",
            "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\Opera Software\*\Cache"), R(@"%LOCALAPPDATA%\Opera Software\*\Default\Cache"),
             R(@"%APPDATA%\Opera Software\*\Code Cache"), R(@"%APPDATA%\Opera Software\*\GPUCache"),
             R(@"%APPDATA%\Opera Software\*\Default\Code Cache"), R(@"%APPDATA%\Opera Software\*\Default\GPUCache")], ["opera"]),
        new("firefox", "Browsers", "Mozilla Firefox", "Disk cache, startup cache and page thumbnails",
            "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2"), R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\startupCache"),
             R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\thumbnails"), R(@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\jumpListCache")],
            ["firefox"]),

        // ---------- Apps ----------
        new("discord", "Apps", "Discord", "Media and web cache", "", JunkKind.Files, true, false,
            Electron(@"%APPDATA%\discord"), ["discord"]),
        new("vscode", "Apps", "Visual Studio Code", "Web caches, cached extension packages and logs", "", JunkKind.Files, true, false,
            Electron(@"%APPDATA%\Code", R(@"%APPDATA%\Code\CachedExtensionVSIXs"), R(@"%APPDATA%\Code\logs"), R(@"%APPDATA%\Code\CachedData")),
            ["Code"]),
        new("slack", "Apps", "Slack", "Media and web cache", "", JunkKind.Files, true, false,
            Electron(@"%APPDATA%\Slack"), ["slack"]),
        new("teams", "Apps", "Microsoft Teams (classic)", "Media and web cache", "", JunkKind.Files, true, false,
            Electron(@"%APPDATA%\Microsoft\Teams"), ["Teams"]),
        new("spotify", "Apps", "Spotify", "Cached songs and artwork", "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\Spotify\Data"), R(@"%LOCALAPPDATA%\Spotify\Browser\Cache")], ["Spotify"]),
        new("steam", "Apps", "Steam", "Built-in browser cache", "", JunkKind.Files, true, false,
            [R(@"%LOCALAPPDATA%\Steam\htmlcache")], ["steam", "steamwebhelper"]),

        // ---------- Developer ----------
        new("npm", "Developer", "npm cache", "Downloaded package tarballs (re-downloaded when needed)",
            "", JunkKind.Files, false, false, [R(@"%LOCALAPPDATA%\npm-cache\_cacache")]),
        new("yarn", "Developer", "Yarn cache", "Downloaded package archives", "", JunkKind.Files, false, false,
            [R(@"%LOCALAPPDATA%\Yarn\Cache")]),
        new("pip", "Developer", "pip cache", "Python wheels and HTTP cache", "", JunkKind.Files, false, false,
            [R(@"%LOCALAPPDATA%\pip\Cache")]),
        new("nuget", "Developer", "NuGet HTTP cache", "Cached package feed responses (not your package folder)",
            "", JunkKind.Files, false, false,
            [R(@"%LOCALAPPDATA%\NuGet\v3-cache"), R(@"%LOCALAPPDATA%\NuGet\plugins-cache")]),

        // ---------- Privacy ----------
        new("recent", "Privacy", "Recent files list", "Shortcuts to recently opened documents and jump lists",
            "", JunkKind.Files, false, false,
            [R(@"%APPDATA%\Microsoft\Windows\Recent", "*.lnk", false),
             R(@"%APPDATA%\Microsoft\Windows\Recent\AutomaticDestinations"),
             R(@"%APPDATA%\Microsoft\Windows\Recent\CustomDestinations")]),
        new("runmru", "Privacy", "Run dialog history", "Commands you typed into Win+R",
            "", JunkKind.RunHistory, false, false, []),
        new("clipboard", "Privacy", "Clipboard", "Whatever is currently copied",
            "", JunkKind.Clipboard, false, false, []),
        new("dns", "Privacy", "DNS cache", "Cached domain lookups; flushing fixes stale-address issues",
            "", JunkKind.DnsCache, true, false, []),
    ];

    // ---------- scanning ----------

    public static void Scan(JunkCategory c, CancellationToken ct = default)
    {
        c.State = ScanState.Scanning;
        c.IsAppRunning = c.Processes.Length > 0 && ProcessService.IsRunning(c.Processes);
        var files = new List<FoundFile>();
        long size = 0;
        int count = 0;

        switch (c.Kind)
        {
            case JunkKind.Files:
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rule in c.Rules)
                foreach (var dir in ExpandDirectories(rule.Path))
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var f in EnumerateFiles(dir, rule, ct))
                    {
                        if (!seen.Add(f.Path)) continue;
                        files.Add(f);
                        size += f.Size;
                    }
                }
                count = files.Count;
                break;

            case JunkKind.RecycleBin:
                var info = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf<Native.SHQUERYRBINFO>() };
                if (Native.SHQueryRecycleBin(null, ref info) == 0)
                {
                    size = info.i64Size;
                    count = (int)info.i64NumItems;
                }
                break;

            case JunkKind.RunHistory:
                using (var k = Registry.CurrentUser.OpenSubKey(RunMruKey))
                    count = k?.GetValueNames().Count(n => n != "MRUList" && n.Length > 0) ?? 0;
                break;

            case JunkKind.Clipboard:
                count = Ui.Run(() =>
                {
                    try { return Clipboard.GetDataObject()?.GetFormats().Length > 0 ? 1 : 0; }
                    catch { return 0; }
                });
                break;

            case JunkKind.DnsCache:
                count = 1;
                break;
        }

        c.Files = files;
        c.Size = size;
        c.Count = count;
        c.State = ScanState.Scanned;
    }

    /// <summary>Expands environment variables and '*' path segments into existing directories.</summary>
    public static IEnumerable<string> ExpandDirectories(string pattern)
    {
        var expanded = Environment.ExpandEnvironmentVariables(pattern);
        var parts = expanded.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) yield break;

        var current = new List<string> { parts[0] + "\\" };
        for (int i = 1; i < parts.Length; i++)
        {
            var next = new List<string>();
            foreach (var basePath in current)
            {
                if (parts[i].Contains('*') || parts[i].Contains('?'))
                {
                    try { next.AddRange(Directory.EnumerateDirectories(basePath, parts[i])); }
                    catch { }
                }
                else next.Add(Path.Combine(basePath, parts[i]));
            }
            current = next;
            if (current.Count == 0) yield break;
        }

        foreach (var d in current)
            if (Directory.Exists(d)) yield return d;
    }

    private static List<FoundFile> EnumerateFiles(string dir, JunkRule rule, CancellationToken ct)
    {
        var result = new List<FoundFile>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = rule.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var cutoff = rule.MinAge.HasValue ? DateTimeOffset.UtcNow - rule.MinAge.Value : DateTimeOffset.MaxValue;
        var pattern = rule.Pattern;

        try
        {
            var e = new FileSystemEnumerable<FoundFile>(dir, (ref FileSystemEntry en) => new FoundFile(en.ToFullPath(), en.Length), options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry en) =>
                    !en.IsDirectory
                    && (pattern == "*" || FileSystemName.MatchesSimpleExpression(pattern, en.FileName))
                    && en.LastWriteTimeUtc < cutoff,
            };
            foreach (var f in e)
            {
                result.Add(f);
                if ((result.Count & 1023) == 0) ct.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // directory vanished or is inaccessible — keep what we found
        }
        return result;
    }

    // ---------- cleaning ----------

    public readonly record struct CleanResult(long Freed, int Deleted, int Failed);

    public static CleanResult Clean(JunkCategory c, CancellationToken ct = default)
    {
        if (c.State == ScanState.Pending) Scan(c, ct);

        if (c.Processes.Length > 0 && ProcessService.IsRunning(c.Processes))
        {
            c.IsAppRunning = true;
            c.State = ScanState.Skipped;
            return default;
        }

        c.State = ScanState.Cleaning;
        long freed = 0;
        int deleted = 0, failed = 0;

        switch (c.Kind)
        {
            case JunkKind.Files:
                var remaining = new List<FoundFile>();
                var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in c.Files)
                {
                    if ((deleted & 255) == 0) ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(f.Path);
                        if (fi.Exists)
                        {
                            if (fi.IsReadOnly) fi.IsReadOnly = false;
                            fi.Delete();
                        }
                        freed += f.Size;
                        deleted++;
                        if (Path.GetDirectoryName(f.Path) is { } parent) touched.Add(parent);
                    }
                    catch
                    {
                        failed++;
                        remaining.Add(f);
                    }
                }
                foreach (var rule in c.Rules.Where(r => r.Recursive))
                foreach (var dir in ExpandDirectories(rule.Path))
                    PruneEmptyDirectories(dir, rule.MinAge, touched);
                c.Files = remaining;
                c.Size = remaining.Sum(f => f.Size);
                c.Count = remaining.Count;
                break;

            case JunkKind.RecycleBin:
                if (c.Count > 0)
                {
                    Native.SHEmptyRecycleBin(IntPtr.Zero, null,
                        Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
                    freed = c.Size;
                    deleted = c.Count;
                }
                c.Size = 0;
                c.Count = 0;
                break;

            case JunkKind.RunHistory:
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(RunMruKey, true);
                    if (k != null)
                        foreach (var name in k.GetValueNames())
                        {
                            k.DeleteValue(name, false);
                            deleted++;
                        }
                }
                catch { failed++; }
                c.Count = 0;
                break;

            case JunkKind.Clipboard:
                Ui.Run(() =>
                {
                    try { Clipboard.Clear(); } catch { }
                });
                deleted = c.Count;
                c.Count = 0;
                break;

            case JunkKind.DnsCache:
                if (Native.DnsFlushResolverCache()) deleted = 1;
                else failed = 1;
                break;
        }

        c.State = ScanState.Cleaned;
        return new CleanResult(freed, deleted, failed);
    }

    /// <summary>
    /// Removes empty subdirectories (never the root). A directory qualifies if it is older than the
    /// grace period, or if this clean emptied it (deleting files bumps the folder's write time).
    /// </summary>
    private static void PruneEmptyDirectories(string root, TimeSpan? minAge, HashSet<string> touched)
    {
        try
        {
            var cutoff = minAge.HasValue ? DateTime.UtcNow - minAge.Value : DateTime.MaxValue;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            var dirs = Directory.EnumerateDirectories(root, "*", options).OrderByDescending(d => d.Length).ToList();
            foreach (var d in dirs)
            {
                try
                {
                    if (!touched.Contains(d) && Directory.GetLastWriteTimeUtc(d) >= cutoff) continue;
                    if (Directory.EnumerateFileSystemEntries(d).Any()) continue;
                    Directory.Delete(d);
                    if (Path.GetDirectoryName(d) is { } parent) touched.Add(parent);
                }
                catch { }
            }
        }
        catch { }
    }
}
