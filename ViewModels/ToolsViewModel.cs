using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class ToolItem : ObservableObject
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required string ActionText { get; init; }
    public bool NeedsAdmin { get; init; }
    public required Func<ToolItem, Task> Action { get; init; }
    public AsyncCommand? Command { get; set; }

    private string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
}

public sealed class PowerPlan : ObservableObject
{
    public required string Guid { get; init; }
    public required string Name { get; init; }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public sealed partial class ToolsViewModel : ObservableObject
{
    private bool _loaded;

    public ToolsViewModel()
    {
        Tools =
        [
            Tool("Free up memory", "Trim idle app memory" + (AdminHelper.IsAdmin ? " and purge the standby list." : "."),
                "", "Optimize", false, async t =>
                {
                    var (trimmed, gained) = await Task.Run(ProcessService.OptimizeMemory);
                    t.Status = gained > 0 ? $"Freed {Fmt.Bytes(gained)}" : $"Trimmed {Fmt.Bytes(trimmed)}";
                }),
            Tool("Flush DNS cache", "Fixes sites that won't load after network or DNS changes.",
                "", "Flush", false, t =>
                {
                    t.Status = Native.DnsFlushResolverCache() ? "DNS cache flushed" : "Couldn't flush DNS";
                    return Task.CompletedTask;
                }),
            Tool("Empty Recycle Bin", "Permanently delete everything in the Recycle Bin.",
                "", "Empty", false, async t =>
                {
                    var cat = JunkEngine.CreateCategories().First(c => c.Kind == JunkKind.RecycleBin);
                    await Task.Run(() => JunkEngine.Scan(cat));
                    if (cat.Count == 0) { t.Status = "Already empty"; return; }
                    if (!Dialogs.Confirm("Empty the Recycle Bin?", $"{cat.Count:N0} items ({Fmt.Bytes(cat.Size)}) will be permanently deleted.", "Empty", true)) return;
                    var r = await Task.Run(() => JunkEngine.Clean(cat));
                    t.Status = $"Freed {Fmt.Bytes(r.Freed)}";
                }),
            Tool("Restart Explorer", "Fixes a frozen taskbar, Start menu or desktop.",
                "", "Restart", false, async t =>
                {
                    await Task.Run(() =>
                    {
                        foreach (var p in Process.GetProcessesByName("explorer"))
                            try { p.Kill(); } catch { }
                        Thread.Sleep(1500);
                        if (Process.GetProcessesByName("explorer").Length == 0) Process.Start("explorer.exe");
                    });
                    t.Status = "Explorer restarted";
                }),
            Tool("Refresh icon cache", "Fixes blank or wrong icons in Explorer and the taskbar.",
                "", "Refresh", false, async t =>
                {
                    await Shell.RunElevatedAsync("ie4uinit.exe", "-show");
                    t.Status = "Icon cache refreshed";
                }),
            Tool("Create restore point", "Snapshot system settings so you can roll back later.",
                "", "Create", true, async t =>
                {
                    t.Status = "Creating…";
                    var code = await Shell.RunElevatedAsync("powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive $env:SystemDrive; Checkpoint-Computer -Description 'Whitebox' -RestorePointType MODIFY_SETTINGS\"");
                    t.Status = code switch { null => "Cancelled", 0 => "Restore point created", _ => "Windows limits restore points to one per day" };
                }),
            Tool("Repair system files", "Runs System File Checker (sfc /scannow) to fix corrupted Windows files.",
                "", "Run SFC", true, async t =>
                {
                    t.Status = "Running in a console window…";
                    var code = await Shell.RunElevatedAsync("cmd.exe", "/k sfc /scannow", hidden: false);
                    t.Status = code == null ? "Cancelled" : "Finished — see the console for results";
                }),
            Tool("Clean component store", "DISM cleanup of superseded Windows Update components.",
                "", "Run DISM", true, async t =>
                {
                    t.Status = "Running in a console window…";
                    var code = await Shell.RunElevatedAsync("cmd.exe", "/k DISM /Online /Cleanup-Image /StartComponentCleanup", hidden: false);
                    t.Status = code == null ? "Cancelled" : "Finished";
                }),
            Tool("Repair Windows image", "DISM RestoreHealth — fixes the image SFC repairs from.",
                "", "Run DISM", true, async t =>
                {
                    t.Status = "Running in a console window…";
                    var code = await Shell.RunElevatedAsync("cmd.exe", "/k DISM /Online /Cleanup-Image /RestoreHealth", hidden: false);
                    t.Status = code == null ? "Cancelled" : "Finished";
                }),
            Tool("Optimize drives", "Open Windows' defrag / TRIM scheduler.",
                "", "Open", false, _ => { Shell.Open("dfrgui.exe"); return Task.CompletedTask; }),
            Tool("Storage Sense", "Let Windows clean temporary files automatically.",
                "", "Open", false, _ => { Shell.Open("ms-settings:storagesense"); return Task.CompletedTask; }),
            Tool("Disk Cleanup", "Classic Windows cleanup, including old Windows installations.",
                "", "Open", false, _ => { Shell.Open("cleanmgr.exe"); return Task.CompletedTask; }),
        ];

        RefreshPlansCommand = new AsyncCommand(LoadPlansAsync);
        ActivatePlanCommand = new AsyncCommand(p => ActivateAsync(p as PowerPlan));
        RestartAdminCommand = new RelayCommand(AdminHelper.RestartElevated);
    }

    private static ToolItem Tool(string title, string description, string glyph, string action, bool admin, Func<ToolItem, Task> run)
    {
        var item = new ToolItem { Title = title, Description = description, Glyph = glyph, ActionText = action, NeedsAdmin = admin, Action = run };
        item.Command = new AsyncCommand(async () =>
        {
            item.IsBusy = true;
            try { await item.Action(item); }
            finally { item.IsBusy = false; }
        });
        return item;
    }

    public List<ToolItem> Tools { get; }
    public ObservableCollection<PowerPlan> Plans { get; } = [];
    public AsyncCommand RefreshPlansCommand { get; }
    public AsyncCommand ActivatePlanCommand { get; }
    public RelayCommand RestartAdminCommand { get; }
    public bool IsAdmin => AdminHelper.IsAdmin;

    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        RefreshPlansCommand.Execute(null);
    }

    [GeneratedRegex(@"GUID:\s*([0-9a-fA-F-]{36})\s+\((.+?)\)\s*(\*)?")]
    private static partial Regex PlanRegex();

    private async Task LoadPlansAsync()
    {
        var output = await Shell.RunCaptureAsync("powercfg.exe", "/list");
        Plans.Clear();
        foreach (Match m in PlanRegex().Matches(output))
            Plans.Add(new PowerPlan { Guid = m.Groups[1].Value, Name = m.Groups[2].Value, IsActive = m.Groups[3].Success });
    }

    private async Task ActivateAsync(PowerPlan? plan)
    {
        if (plan == null) return;
        await Shell.RunCaptureAsync("powercfg.exe", $"/setactive {plan.Guid}");
        await LoadPlansAsync();
        Notifier.Show("Power plan changed", plan.Name, ToastKind.Success);
    }
}
