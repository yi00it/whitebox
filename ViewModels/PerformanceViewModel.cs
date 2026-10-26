using System.Collections.ObjectModel;
using System.Windows.Threading;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class ProcessRow : ObservableObject
{
    public int Pid { get; init; }
    public required string Name { get; init; }
    public string DisplayName => Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Name[..^4] : Name;

    internal long LastCpuTime;

    private double _cpu;
    public double Cpu { get => _cpu; set { if (Set(ref _cpu, value)) OnPropertyChanged(nameof(CpuText)); } }

    private long _memory;
    public long Memory { get => _memory; set { if (Set(ref _memory, value)) OnPropertyChanged(nameof(MemoryText)); } }

    private int _threads;
    public int Threads { get => _threads; set => Set(ref _threads, value); }

    public string CpuText => Cpu < 0.05 ? "0%" : Cpu < 10 ? $"{Cpu:0.0}%" : $"{Cpu:0}%";
    public string MemoryText => Fmt.Bytes(Memory);
}

public sealed class PerformanceViewModel : ObservableObject
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, ProcessRow> _rows = [];
    private DateTime _lastSample;
    private bool _sampling;

    public PerformanceViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        EndTaskCommand = new RelayCommand(EndTask, () => Selected != null);
        OptimizeCommand = new AsyncCommand(OptimizeAsync);
    }

    public SystemMonitor Monitor => SystemMonitor.Instance;
    public RelayCommand EndTaskCommand { get; }
    public AsyncCommand OptimizeCommand { get; }

    public string CpuName => SystemInfo.CpuName;
    public string[] SortOptions { get; } = ["CPU", "Memory", "Name"];

    private string _sortBy = "Memory";
    public string SortBy { get => _sortBy; set { if (Set(ref _sortBy, value)) ApplyView(); } }

    private string _filter = "";
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) ApplyView(); } }

    private ObservableCollection<ProcessRow> _processes = [];
    public ObservableCollection<ProcessRow> Processes { get => _processes; private set => Set(ref _processes, value); }

    private ProcessRow? _selected;
    public ProcessRow? Selected { get => _selected; set { Set(ref _selected, value); Ui.Requery(); } }

    private int _processCount;
    public int ProcessCount { get => _processCount; set => Set(ref _processCount, value); }

    private int _threadCount;
    public int ThreadCount { get => _threadCount; set => Set(ref _threadCount, value); }

    private string _optimizeStatus = "";
    public string OptimizeStatus { get => _optimizeStatus; set => Set(ref _optimizeStatus, value); }

    public void SetActive(bool active)
    {
        if (active)
        {
            _ = RefreshAsync();
            _timer.Start();
        }
        else _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            var snapshot = await Task.Run(ProcessService.Snapshot);
            var now = DateTime.UtcNow;
            double elapsed100ns = _lastSample == default ? 0 : (now - _lastSample).Ticks * (double)Environment.ProcessorCount;
            _lastSample = now;

            var alive = new HashSet<int>();
            foreach (var s in snapshot)
            {
                alive.Add(s.Pid);
                if (!_rows.TryGetValue(s.Pid, out var row))
                {
                    row = new ProcessRow { Pid = s.Pid, Name = s.Name, LastCpuTime = s.CpuTime };
                    _rows[s.Pid] = row;
                }
                else if (elapsed100ns > 0)
                {
                    row.Cpu = Math.Clamp((s.CpuTime - row.LastCpuTime) * 100.0 / elapsed100ns, 0, 100);
                }
                row.LastCpuTime = s.CpuTime;
                row.Memory = s.PrivateBytes > 0 ? s.PrivateBytes : s.WorkingSet;
                row.Threads = s.Threads;
            }
            foreach (var pid in _rows.Keys.Where(k => !alive.Contains(k)).ToList()) _rows.Remove(pid);

            ProcessCount = snapshot.Count;
            ThreadCount = snapshot.Sum(s => s.Threads);
            ApplyView();
        }
        finally
        {
            _sampling = false;
        }
    }

    private void ApplyView()
    {
        IEnumerable<ProcessRow> rows = _rows.Values;
        if (!string.IsNullOrWhiteSpace(Filter))
            rows = rows.Where(r => r.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) || r.Pid.ToString() == Filter);
        rows = SortBy switch
        {
            "CPU" => rows.OrderByDescending(r => r.Cpu).ThenByDescending(r => r.Memory),
            "Name" => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(r => r.Memory),
        };
        var list = rows.ToList();
        var selected = Selected;

        if (list.Count == Processes.Count && list.SequenceEqual(Processes)) return;
        Processes = new ObservableCollection<ProcessRow>(list);
        if (selected != null && _rows.ContainsValue(selected)) Selected = selected;
    }

    private void EndTask()
    {
        var p = Selected;
        if (p == null) return;
        if (!Dialogs.Confirm($"End {p.DisplayName}?", "Unsaved data in this app will be lost.", "End task", true)) return;
        if (ProcessService.Kill(p.Pid))
        {
            Notifier.Show("Task ended", p.DisplayName, ToastKind.Success);
            _rows.Remove(p.Pid);
            Selected = null;
            ApplyView();
        }
        else Notifier.Show("Couldn't end task", "Access denied — the process may be protected or need administrator rights.", ToastKind.Warning);
    }

    private async Task OptimizeAsync()
    {
        OptimizeStatus = "Optimizing…";
        var (trimmed, gained) = await Task.Run(ProcessService.OptimizeMemory);
        OptimizeStatus = gained > 0 ? $"Freed {Fmt.Bytes(gained)}" : $"Trimmed {Fmt.Bytes(trimmed)}";
        Notifier.Show("Memory optimized",
            $"Trimmed {Fmt.Bytes(trimmed)} from app working sets" + (gained > 0 ? $", {Fmt.Bytes(gained)} more memory available." : "."),
            ToastKind.Success);
        await RefreshAsync();
    }
}
