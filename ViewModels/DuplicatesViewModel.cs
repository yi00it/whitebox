using System.Collections.ObjectModel;
using System.ComponentModel;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class DuplicatesViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    public DuplicatesViewModel()
    {
        Locations = new ObservableCollection<LocationOption>(new[] { UserFiles() }.Concat(LocationOption.Drives()));
        Location = Locations[0];
        Size = Sizes[1];
        ScanCommand = new AsyncCommand(ScanAsync, () => Location != null && State != FinderState.Scanning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        BrowseCommand = new RelayCommand(Browse);
        RevealCommand = new RelayCommand(p => { if (p is FileItem f) Shell.Reveal(f.Path); });
        AutoSelectCommand = new RelayCommand(AutoSelect, () => Groups.Count > 0);
        RecycleCommand = new AsyncCommand(RecycleAsync, () => SelectedCount > 0);
    }

    private static LocationOption UserFiles() => new()
    {
        Label = "Your files",
        Path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    };

    public ObservableCollection<LocationOption> Locations { get; }
    public SizeOption[] Sizes { get; } =
    [
        new() { Label = "100 KB+", Bytes = 100L << 10 },
        new() { Label = "1 MB+", Bytes = 1L << 20 },
        new() { Label = "10 MB+", Bytes = 10L << 20 },
        new() { Label = "100 MB+", Bytes = 100L << 20 },
    ];

    public AsyncCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand AutoSelectCommand { get; }
    public AsyncCommand RecycleCommand { get; }

    private LocationOption? _location;
    public LocationOption? Location { get => _location; set { Set(ref _location, value); Ui.Requery(); } }

    private SizeOption? _size;
    public SizeOption? Size { get => _size; set => Set(ref _size, value); }

    private FinderState _state;
    public FinderState State { get => _state; set { Set(ref _state, value); Ui.Requery(); } }

    private string _status = "Downloads, photos and backups often hide duplicate copies.";
    public string Status { get => _status; set => Set(ref _status, value); }

    private ObservableCollection<DuplicateGroup> _groups = [];
    public ObservableCollection<DuplicateGroup> Groups { get => _groups; private set => Set(ref _groups, value); }

    public long Wasted => Groups.Sum(g => g.Wasted);
    public int SelectedCount => Groups.Sum(g => g.Files.Count(f => f.IsChecked));
    public long SelectedSize => Groups.Sum(g => g.Files.Where(f => f.IsChecked).Sum(f => f.Size));

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
        Status = "Indexing files…";
        var root = Location.Path;
        long min = Size?.Bytes ?? 1L << 20;
        string stage = "Indexing files…";
        try
        {
            var groups = await Task.Run(() => FileScanner.FindDuplicates(root, min, _cts.Token,
                s => Ui.Run(() => Status = stage = s),
                n => Ui.Run(() => Status = $"{stage} {n:N0} files checked")));
            foreach (var f in Groups.SelectMany(g => g.Files)) f.PropertyChanged -= OnFileChanged;
            Groups = new ObservableCollection<DuplicateGroup>(groups);
            foreach (var f in Groups.SelectMany(g => g.Files)) f.PropertyChanged += OnFileChanged;
            Status = groups.Count == 0
                ? "No duplicates found."
                : $"{groups.Count:N0} sets of duplicates · {Fmt.Bytes(Wasted)} reclaimable";
            State = FinderState.Results;
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
            State = Groups.Count > 0 ? FinderState.Results : FinderState.Idle;
        }
        Raise();
    }

    private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.IsChecked)) Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(Wasted));
        Ui.Requery();
    }

    /// <summary>Selects every copy except the oldest one in each set.</summary>
    private void AutoSelect()
    {
        foreach (var g in Groups)
        {
            var keep = g.Files.OrderBy(f => f.Modified).First();
            foreach (var f in g.Files) f.IsChecked = f != keep;
        }
    }

    private async Task RecycleAsync()
    {
        var selected = Groups.SelectMany(g => g.Files).Where(f => f.IsChecked).ToList();
        if (selected.Count == 0) return;
        var wholeGroups = Groups.Where(g => g.Files.All(f => f.IsChecked)).ToList();
        var warning = wholeGroups.Count > 0
            ? $"\n\nWarning: every copy is selected in {wholeGroups.Count} set(s) — no copy of those files will remain."
            : "";
        if (!Dialogs.Confirm($"Move {selected.Count:N0} duplicates to the Recycle Bin?",
                $"{Fmt.Bytes(selected.Sum(f => f.Size))} will be moved to the Recycle Bin.{warning}", "Move to Recycle Bin", true))
            return;

        var removed = new HashSet<string>(await Task.Run(() => Shell.Recycle(selected.Select(f => f.Path).ToList())),
            StringComparer.OrdinalIgnoreCase);
        var updated = new List<DuplicateGroup>();
        foreach (var g in Groups)
        {
            var remaining = g.Files.Where(f => !removed.Contains(f.Path)).ToList();
            if (remaining.Count > 1) updated.Add(new DuplicateGroup { Size = g.Size, Files = remaining });
        }
        Groups = new ObservableCollection<DuplicateGroup>(updated);
        Status = $"{updated.Count:N0} sets of duplicates · {Fmt.Bytes(Wasted)} reclaimable";
        Raise();
        Notifier.Show("Moved to Recycle Bin", $"{removed.Count:N0} files · {Fmt.Bytes(selected.Where(f => removed.Contains(f.Path)).Sum(f => f.Size))}", ToastKind.Success);
    }
}
