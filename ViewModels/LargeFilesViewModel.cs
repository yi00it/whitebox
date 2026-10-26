using System.Collections.ObjectModel;
using System.ComponentModel;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class KindFilter
{
    public required string Name { get; init; }
    public int Count { get; init; }
    public long Size { get; init; }
    public string Label => Name == "All" ? $"All  {Count:N0}" : $"{Name}  {Count:N0}";
}

public enum FinderState { Idle, Scanning, Results }

public sealed class LargeFilesViewModel : ObservableObject
{
    private List<FileItem> _all = [];
    private CancellationTokenSource? _cts;

    public LargeFilesViewModel()
    {
        Locations = new ObservableCollection<LocationOption>(LocationOption.Drives());
        Location = Locations.FirstOrDefault();
        Size = Sizes[1];
        ScanCommand = new AsyncCommand(ScanAsync, () => Location != null && State != FinderState.Scanning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        BrowseCommand = new RelayCommand(Browse);
        RevealCommand = new RelayCommand(p => { if (p is FileItem f) Shell.Reveal(f.Path); });
        RecycleCommand = new AsyncCommand(RecycleAsync, () => SelectedCount > 0);
        ClearSelectionCommand = new RelayCommand(() => { foreach (var f in _all) f.IsChecked = false; });
    }

    public ObservableCollection<LocationOption> Locations { get; }
    public SizeOption[] Sizes { get; } =
    [
        new() { Label = "50 MB+", Bytes = 50L << 20 },
        new() { Label = "100 MB+", Bytes = 100L << 20 },
        new() { Label = "500 MB+", Bytes = 500L << 20 },
        new() { Label = "1 GB+", Bytes = 1L << 30 },
    ];

    public AsyncCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RevealCommand { get; }
    public AsyncCommand RecycleCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }

    private LocationOption? _location;
    public LocationOption? Location { get => _location; set { Set(ref _location, value); Ui.Requery(); } }

    private SizeOption? _size;
    public SizeOption? Size { get => _size; set => Set(ref _size, value); }

    private FinderState _state;
    public FinderState State { get => _state; set { Set(ref _state, value); Ui.Requery(); } }

    private string _status = "Large, forgotten files are the fastest way to win back space.";
    public string Status { get => _status; set => Set(ref _status, value); }

    private ObservableCollection<FileItem> _files = [];
    public ObservableCollection<FileItem> Files { get => _files; private set => Set(ref _files, value); }

    private List<KindFilter> _kinds = [];
    public List<KindFilter> Kinds { get => _kinds; private set => Set(ref _kinds, value); }

    private KindFilter? _kind;
    public KindFilter? Kind { get => _kind; set { if (Set(ref _kind, value)) ApplyFilter(); } }

    public int SelectedCount => _all.Count(f => f.IsChecked);
    public long SelectedSize => _all.Where(f => f.IsChecked).Sum(f => f.Size);
    public long TotalSize => _all.Sum(f => f.Size);

    private void Browse()
    {
        var opt = LocationOption.Browse();
        if (opt == null) return;
        Locations.Add(opt);
        Location = opt;
    }

    private async Task ScanAsync()
    {
        if (Location == null) return;
        _cts = new CancellationTokenSource();
        State = FinderState.Scanning;
        Status = "Starting…";
        var root = Location.Path;
        long min = Size?.Bytes ?? 100L << 20;
        try
        {
            var found = await Task.Run(() => FileScanner.FindLarge(root, min, _cts.Token,
                n => Ui.Run(() => Status = $"Checked {n:N0} files…")));
            foreach (var f in _all) f.PropertyChanged -= OnFileChanged;
            _all = found;
            foreach (var f in _all) f.PropertyChanged += OnFileChanged;

            var kinds = new List<KindFilter> { new() { Name = "All", Count = _all.Count, Size = TotalSize } };
            kinds.AddRange(_all.GroupBy(f => f.Kind).OrderByDescending(g => g.Sum(f => f.Size))
                .Select(g => new KindFilter { Name = g.Key, Count = g.Count(), Size = g.Sum(f => f.Size) }));
            Kinds = kinds;
            Kind = kinds[0];
            ApplyFilter();
            Status = $"{_all.Count:N0} files · {Fmt.Bytes(TotalSize)}";
            State = FinderState.Results;
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
            State = _all.Count > 0 ? FinderState.Results : FinderState.Idle;
        }
        RaiseSelection();
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.IsChecked)) RaiseSelection();
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(TotalSize));
        Ui.Requery();
    }

    private void ApplyFilter()
    {
        var k = Kind?.Name ?? "All";
        Files = new ObservableCollection<FileItem>(k == "All" ? _all : _all.Where(f => f.Kind == k));
    }

    private async Task RecycleAsync()
    {
        var selected = _all.Where(f => f.IsChecked).ToList();
        if (selected.Count == 0) return;
        if (!Dialogs.Confirm($"Move {selected.Count:N0} files to the Recycle Bin?",
                $"{Fmt.Bytes(selected.Sum(f => f.Size))} will be moved to the Recycle Bin. You can restore them from there.",
                "Move to Recycle Bin", true))
            return;

        var removed = await Task.Run(() => Shell.Recycle(selected.Select(f => f.Path).ToList()));
        var set = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
        var gone = selected.Where(f => set.Contains(f.Path)).ToList();
        foreach (var f in gone)
        {
            f.PropertyChanged -= OnFileChanged;
            _all.Remove(f);
        }
        ApplyFilter();
        RaiseSelection();
        Notifier.Show("Moved to Recycle Bin", $"{gone.Count:N0} files · {Fmt.Bytes(gone.Sum(f => f.Size))}", ToastKind.Success);
    }
}
