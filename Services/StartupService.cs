using System.Diagnostics;
using System.Windows.Media;
using Microsoft.Win32;
using Whitebox.Core;

namespace Whitebox.Services;

public sealed class StartupItem : ObservableObject
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public required string Location { get; init; }
    public string? ExePath { get; init; }
    public string? Publisher { get; init; }

    internal RegistryHive ApprovedHive { get; init; }
    internal string ApprovedKey { get; init; } = "";
    internal string ApprovedValue { get; init; } = "";
    public bool CanChange { get; init; }

    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            if (!StartupService.SetEnabled(this, value))
            {
                Notifier.Show("Couldn't change startup item",
                    CanChange ? "Access was denied." : "This item applies to all users — restart Whitebox as administrator.",
                    ToastKind.Warning);
                OnPropertyChanged();
                return;
            }
            _isEnabled = value;
            OnPropertyChanged();
            StartupService.RaiseChanged();
        }
    }

    internal void InitEnabled(bool value) => _isEnabled = value;
}

public static class StartupService
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static event Action? Changed;
    internal static void RaiseChanged() => Changed?.Invoke();

    public static List<StartupItem> GetItems()
    {
        var items = new List<StartupItem>();

        ReadRegistry(items, RegistryHive.CurrentUser, RegistryView.Default, $@"{Approved}\Run", "Current user");
        ReadRegistry(items, RegistryHive.LocalMachine, RegistryView.Registry64, $@"{Approved}\Run", "All users");
        ReadRegistry(items, RegistryHive.LocalMachine, RegistryView.Registry32, $@"{Approved}\Run32", "All users (32-bit)");

        ReadFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Startup folder");
        ReadFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Startup folder (all users)");

        return items.OrderByDescending(i => i.IsEnabled).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ReadRegistry(List<StartupItem> items, RegistryHive hive, RegistryView view, string approvedKey, string location)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var run = root.OpenSubKey(RunKey);
            if (run == null) return;
            bool canChange = hive == RegistryHive.CurrentUser || AdminHelper.IsAdmin;
            foreach (var name in run.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var cmd = run.GetValue(name)?.ToString() ?? "";
                var exe = IconLoader.ExtractPath(cmd);
                var item = new StartupItem
                {
                    Name = name,
                    Command = cmd,
                    Location = location,
                    ExePath = exe,
                    Publisher = GetPublisher(exe),
                    ApprovedHive = hive,
                    ApprovedKey = approvedKey,
                    ApprovedValue = name,
                    CanChange = canChange,
                };
                item.InitEnabled(ReadApproved(hive, approvedKey, name));
                items.Add(item);
            }
        }
        catch { }
    }

    private static void ReadFolder(List<StartupItem> items, string folder, RegistryHive hive, string location)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            bool canChange = hive == RegistryHive.CurrentUser || AdminHelper.IsAdmin;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var target = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcut(file) ?? file : file;
                var item = new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = target,
                    Location = location,
                    ExePath = target,
                    Publisher = GetPublisher(target),
                    ApprovedHive = hive,
                    ApprovedKey = $@"{Approved}\StartupFolder",
                    ApprovedValue = fileName,
                    CanChange = canChange,
                };
                item.InitEnabled(ReadApproved(hive, item.ApprovedKey, fileName));
                items.Add(item);
            }
        }
        catch { }
    }

    private static string? ResolveShortcut(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetPublisher(string? exe)
    {
        try
        {
            if (exe == null || !File.Exists(exe)) return null;
            var company = FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim();
            return string.IsNullOrEmpty(company) ? null : company;
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadApproved(RegistryHive hive, string key, string value)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = root.OpenSubKey(key);
            if (k?.GetValue(value) is byte[] { Length: > 0 } data) return (data[0] & 1) == 0;
        }
        catch { }
        return true;
    }

    internal static bool SetEnabled(StartupItem item, bool enabled)
    {
        if (!item.CanChange) return false;
        try
        {
            using var root = RegistryKey.OpenBaseKey(item.ApprovedHive, RegistryView.Registry64);
            using var k = root.CreateSubKey(item.ApprovedKey, true);
            var data = new byte[12];
            data[0] = enabled ? (byte)0x02 : (byte)0x03;
            if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            k.SetValue(item.ApprovedValue, data, RegistryValueKind.Binary);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
