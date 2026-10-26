using System.Collections.ObjectModel;
using Whitebox.Controls;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class SpaceEntry
{
    public required TreeMapItem Item { get; init; }
    public double Fraction { get; init; }
    public string Name => Item.Name;
    public string SizeText => Fmt.Bytes(Item.Size);
    public string Glyph => Item.IsAggregate ? "" : Item.IsDirectory ? "" : FileKinds.Glyph(FileKinds.Classify(Item.Path));
    public string PercentText => $"{Fraction * 100:0.0}%";
}

public sealed class SpaceLensViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private DirNode? _root;

    public SpaceLensViewModel()
    {
        Locations = new ObservableCollection<LocationOption>(LocationOption.Drives());
        Location = Locations.FirstOrDefault();
        ScanCommand = new AsyncCommand(ScanAsync, () => Location != null && State != FinderState.Scanning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
        BrowseCommand = new RelayCommand(Browse);
        OpenCommand = new RelayCommand(Open);
        UpCommand = new RelayCommand(() => { if (Current?.Parent != null) Show(Current.Parent); }, () => Current?.Parent != null);
        RevealCommand = new RelayCommand(() =>
        {
            if (Selected is { } s) Shell.Reveal(s.Item.Path);
            else if (Current != null) Shell.Open(Current.Path);
        });
        RecycleCommand = new AsyncCommand(RecycleAsync, () => Selected is { Item.IsAggregate: false });
    }

    public ObservableCollection<LocationOption> Locations { get; }
    public AsyncCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand UpCommand { get; }
    public RelayCommand RevealCommand { get; }
    public AsyncCommand RecycleCommand { get; }

    private LocationOption? _location;
    public LocationOption? Location { get => _location; set { Set(ref _location, value); Ui.Requery(); } }

    private FinderState _state;
    public FinderState State { get => _state; set { Set(ref _state, value); Ui.Requery(); } }

    private string _status = "A visual map of every folder, sized by how much space it uses.";
    public string Status { get => _status; set => Set(ref _status, value); }

    private DirNode? _current;
    public DirNode? Current { get => _current; private set { Set(ref _current, value); OnPropertyChanged(nameof(Breadcrumb)); OnPropertyChanged(nameof(CurrentSize)); } }

    public string Breadcrumb => Current?.Path ?? "";
    public string CurrentSize => Current != null ? Fmt.Bytes(Current.Size) : "";

    private IReadOnlyList<TreeMapItem>? _items;
    public IReadOnlyList<TreeMapItem>? Items { get => _items; private set => Set(ref _items, value); }

    private List<SpaceEntry> _entries = [];
    public List<SpaceEntry> Entries { get => _entries; private set => Set(ref _entries, value); }

    private SpaceEntry? _selected;
    public SpaceEntry? Selected { get => _selected; set { Set(ref _selected, value); Ui.Requery(); } }

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
        Status = "Measuring folders…";
        var path = Location.Path;
        try
        {
            _root = await Task.Run(() => SpaceAnalyzer.Scan(path, _cts.Token,
                n => Ui.Run(() => Status = $"Measured {n:N0} files…")));
            Show(_root);
            State = FinderState.Results;
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
            State = _root != null ? FinderState.Results : FinderState.Idle;
        }
    }

    private void Show(DirNode node)
    {
        Current = node;
        Selected = null;
        var items = SpaceAnalyzer.ItemsFor(node);
        Items = items;
        double total = Math.Max(1, node.Size);
        Entries = items.Take(60).Select(i => new SpaceEntry { Item = i, Fraction = i.Size / total }).ToList();
        Ui.Requery();
    }

    private void Open(object? parameter)
    {
        var item = parameter switch
        {
            TreeMapItem t => t,
            SpaceEntry e => e.Item,
            _ => null,
        };
        if (item == null || Current == null) return;
        if (item.IsDirectory)
        {
            var child = Current.Children.FirstOrDefault(c => c.Path == item.Path);
            if (child != null) Show(child);
        }
        else
        {
            Selected = Entries.FirstOrDefault(e => e.Item == item) ?? new SpaceEntry { Item = item, Fraction = item.Size / (double)Math.Max(1, Current.Size) };
        }
    }

    private async Task RecycleAsync()
    {
        var s = Selected;
        if (s == null || Current == null) return;
        if (!Dialogs.Confirm($"Move \"{s.Name}\" to the Recycle Bin?", $"{s.SizeText} will be moved to the Recycle Bin.", "Move to Recycle Bin", true))
            return;
        var removed = await Task.Run(() => Shell.Recycle([s.Item.Path]));
        if (removed.Count == 0)
        {
            Notifier.Show("Couldn't remove", "The item may be in use or protected.", ToastKind.Warning);
            return;
        }

        // Update the tree sizes up the chain without rescanning.
        var node = Current;
        var dir = node.Children.FirstOrDefault(c => c.Path == s.Item.Path);
        if (dir != null) node.Children.Remove(dir);
        else node.FileBytes -= s.Item.Size;
        for (var n = node; n != null; n = n.Parent) n.Size -= s.Item.Size;
        Show(node);
        Notifier.Show("Moved to Recycle Bin", $"{s.Name} · {s.SizeText}", ToastKind.Success);
    }
}
