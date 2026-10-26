using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public enum CareState { Idle, Scanning, Results, Cleaning, Done }

public sealed class HomeViewModel : ObservableObject
{
    private List<JunkCategory> _categories = [];

    public HomeViewModel()
    {
        ScanCommand = new AsyncCommand(ScanAsync);
        RunCommand = new AsyncCommand(RunAsync, () => State == CareState.Results && (DoClean || DoMemory));
        ResetCommand = new RelayCommand(() => State = CareState.Idle);
        GoCommand = new RelayCommand(p => Navigator.Go(p as string ?? "home"));
        RefreshDrive();
    }

    public SystemMonitor Monitor => SystemMonitor.Instance;
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand RunCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand GoCommand { get; }

    public string Greeting
    {
        get
        {
            int h = DateTime.Now.Hour;
            var part = h < 5 ? "Good evening" : h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
            return $"{part}, {SystemInfo.UserName}";
        }
    }

    public string OsName => SystemInfo.OsName;
    public string CpuName => SystemInfo.CpuName;
    public string MachineName => SystemInfo.MachineName;
    public string CoresText => $"{SystemInfo.Cores} logical processors";

    private CareState _state;
    public CareState State { get => _state; set { if (Set(ref _state, value)) Ui.Requery(); } }

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private string _step = "";
    public string Step { get => _step; set => Set(ref _step, value); }

    private long _junkSize;
    public long JunkSize { get => _junkSize; set => Set(ref _junkSize, value); }

    private int _junkCount;
    public int JunkCount { get => _junkCount; set => Set(ref _junkCount, value); }

    private string _junkDetail = "";
    public string JunkDetail { get => _junkDetail; set => Set(ref _junkDetail, value); }

    private int _startupEnabled;
    public int StartupEnabled { get => _startupEnabled; set => Set(ref _startupEnabled, value); }

    private double _ramAtScan;
    public double RamAtScan { get => _ramAtScan; set => Set(ref _ramAtScan, value); }

    private bool _doClean = true;
    public bool DoClean { get => _doClean; set { Set(ref _doClean, value); Ui.Requery(); } }

    private bool _doMemory = true;
    public bool DoMemory { get => _doMemory; set { Set(ref _doMemory, value); Ui.Requery(); } }

    private string _doneTitle = "";
    public string DoneTitle { get => _doneTitle; set => Set(ref _doneTitle, value); }

    private string _doneDetail = "";
    public string DoneDetail { get => _doneDetail; set => Set(ref _doneDetail, value); }

    private string _driveName = "";
    public string DriveName { get => _driveName; set => Set(ref _driveName, value); }

    private double _driveUsed;
    public double DriveUsed { get => _driveUsed; set => Set(ref _driveUsed, value); }

    private string _driveText = "";
    public string DriveText { get => _driveText; set => Set(ref _driveText, value); }

    private void RefreshDrive()
    {
        try
        {
            var d = SystemInfo.SystemDrive;
            DriveName = $"Disk {d.Name.TrimEnd('\\')}";
            DriveUsed = d.TotalSize > 0 ? 1 - (double)d.AvailableFreeSpace / d.TotalSize : 0;
            DriveText = $"{Fmt.Bytes(d.AvailableFreeSpace)} free on {d.Name.TrimEnd('\\')}";
        }
        catch { }
    }

    private async Task ScanAsync()
    {
        State = CareState.Scanning;
        Progress = 0;
        _categories = JunkEngine.CreateCategories().Where(c => c.IsChecked).ToList();
        int total = _categories.Count + 2, done = 0;

        foreach (var c in _categories)
        {
            Step = $"Looking for {c.Name.ToLowerInvariant()}…";
            await Task.Run(() => JunkEngine.Scan(c));
            Progress = ++done / (double)total;
        }

        Step = "Checking startup apps…";
        var items = await Task.Run(StartupService.GetItems);
        StartupEnabled = items.Count(i => i.IsEnabled);
        Progress = ++done / (double)total;

        Step = "Analyzing memory…";
        await Task.Delay(350);
        RamAtScan = Monitor.RamPercent;
        Progress = 1;

        JunkSize = _categories.Sum(c => c.Size);
        JunkCount = _categories.Sum(c => c.Kind == JunkKind.DnsCache ? 0 : c.Count);
        var top = _categories.Where(c => c.Size > 0).OrderByDescending(c => c.Size).Take(3).Select(c => c.Name);
        JunkDetail = JunkSize > 0 ? "Mostly " + string.Join(", ", top) : "Your system is already tidy";
        RefreshDrive();
        await Task.Delay(250);
        State = CareState.Results;
    }

    private async Task RunAsync()
    {
        State = CareState.Cleaning;
        Progress = 0;
        long freed = 0;
        int steps = (DoClean ? _categories.Count : 0) + (DoMemory ? 1 : 0), done = 0;
        var skipped = new List<string>();

        if (DoClean)
        {
            foreach (var c in _categories)
            {
                Step = $"Cleaning {c.Name.ToLowerInvariant()}…";
                var r = await Task.Run(() => JunkEngine.Clean(c));
                if (c.State == ScanState.Skipped) skipped.Add(c.Name);
                freed += r.Freed;
                Progress = ++done / (double)steps;
            }
        }

        (long Trimmed, long Gained) mem = default;
        if (DoMemory)
        {
            Step = "Optimizing memory…";
            mem = await Task.Run(ProcessService.OptimizeMemory);
            Progress = 1;
        }

        RefreshDrive();
        DoneTitle = DoClean && freed > 0 ? $"{Fmt.Bytes(freed)} cleaned" : "All done";
        var parts = new List<string>();
        if (DoClean) parts.Add(freed > 0 ? $"Removed {Fmt.Bytes(freed)} of junk" : "No junk needed removing");
        if (DoMemory) parts.Add($"trimmed {Fmt.Bytes(mem.Trimmed)} of app memory");
        if (skipped.Count > 0) parts.Add($"skipped {string.Join(", ", skipped)} (still running)");
        DoneDetail = string.Join(" · ", parts) + ".";
        await Task.Delay(300);
        State = CareState.Done;
        Notifier.Show("Smart Care complete", DoneDetail, ToastKind.Success);
    }
}
