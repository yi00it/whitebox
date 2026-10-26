using System.ComponentModel;
using System.Windows.Data;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public enum CleanupState { Idle, Scanning, Scanned, Cleaning, Cleaned }

public sealed class CleanupViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    public CleanupViewModel()
    {
        Categories = JunkEngine.CreateCategories();
        foreach (var c in Categories) c.PropertyChanged += OnCategoryChanged;
        View = new ListCollectionView(Categories);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(JunkCategory.Group)));

        ScanCommand = new AsyncCommand(ScanAsync, () => !IsBusy);
        CleanCommand = new AsyncCommand(CleanAsync, () => !IsBusy && SelectedSize + SelectedOther > 0);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        SelectAllCommand = new RelayCommand(() => SetAll(true), () => !IsBusy);
        SelectNoneCommand = new RelayCommand(() => SetAll(false), () => !IsBusy);
        RevealCommand = new RelayCommand(p => { if (p is FoundFile f) Shell.Reveal(f.Path); });
        RestartAdminCommand = new RelayCommand(AdminHelper.RestartElevated);
    }

    public List<JunkCategory> Categories { get; }
    public ListCollectionView View { get; }
    public AsyncCommand ScanCommand { get; }
    public AsyncCommand CleanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand RestartAdminCommand { get; }

    public bool IsAdmin => AdminHelper.IsAdmin;

    private CleanupState _state;
    public CleanupState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsBusy));
            Ui.Requery();
        }
    }

    public bool IsBusy => State is CleanupState.Scanning or CleanupState.Cleaning;

    private double _progress;
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    private string _status = "Scan to find files you can safely remove.";
    public string Status { get => _status; set => Set(ref _status, value); }

    public long TotalFound => Categories.Sum(c => c.Size);
    public long SelectedSize => Categories.Where(c => c.IsChecked).Sum(c => c.Size);
    private int SelectedOther => Categories.Count(c => c.IsChecked && c.State == ScanState.Scanned && c.Size == 0 && c.Count > 0);
    public int SelectedCount => Categories.Count(c => c.IsChecked);

    private JunkCategory? _selected;
    public JunkCategory? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(SelectedFiles));
        }
    }

    public IReadOnlyList<FoundFile> SelectedFiles => Selected?.Preview ?? [];

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(JunkCategory.IsChecked) or nameof(JunkCategory.Size) or nameof(JunkCategory.Count))
        {
            OnPropertyChanged(nameof(SelectedSize));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(TotalFound));
            Ui.Requery();
        }
        if (sender == Selected && e.PropertyName == nameof(JunkCategory.State))
            OnPropertyChanged(nameof(SelectedFiles));
    }

    private void SetAll(bool value)
    {
        foreach (var c in Categories) c.IsChecked = value;
    }

    private async Task ScanAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        State = CleanupState.Scanning;
        Progress = 0;
        foreach (var c in Categories) c.State = ScanState.Pending;

        var targets = Categories.Where(c => c.IsAvailable).ToList();
        int done = 0;
        try
        {
            await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (c, token) =>
            {
                await Task.Run(() => JunkEngine.Scan(c, token), token);
                int n = Interlocked.Increment(ref done);
                Ui.Run(() =>
                {
                    Progress = n / (double)targets.Count;
                    Status = $"Scanned {n} of {targets.Count} locations…";
                });
            });
            Status = TotalFound > 0
                ? $"Found {Fmt.Bytes(TotalFound)} across {Categories.Count(c => c.Size > 0)} locations."
                : "Nothing to clean — your PC is spotless.";
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
            foreach (var c in Categories.Where(c => c.State == ScanState.Scanning)) c.State = ScanState.Pending;
        }
        finally
        {
            State = CleanupState.Scanned;
            OnPropertyChanged(nameof(SelectedFiles));
            OnPropertyChanged(nameof(TotalFound));
            OnPropertyChanged(nameof(SelectedSize));
        }
    }

    private async Task CleanAsync()
    {
        var targets = Categories.Where(c => c.IsChecked && (c.Size > 0 || c.Count > 0)).ToList();
        if (targets.Count == 0) return;
        if (!Dialogs.Confirm("Clean selected items?",
                $"Whitebox will permanently remove {Fmt.Bytes(targets.Sum(t => t.Size))} from {targets.Count} locations. Files in use are skipped automatically.",
                "Clean now"))
            return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        State = CleanupState.Cleaning;
        Progress = 0;
        long freed = 0;
        int failed = 0, done = 0;
        var skipped = new List<string>();

        try
        {
            foreach (var c in targets)
            {
                Status = $"Cleaning {c.Name}…";
                var r = await Task.Run(() => JunkEngine.Clean(c, ct), ct);
                freed += r.Freed;
                failed += r.Failed;
                if (c.State == ScanState.Skipped) skipped.Add(c.Name);
                Progress = ++done / (double)targets.Count;
            }
        }
        catch (OperationCanceledException)
        {
        }

        Status = $"Freed {Fmt.Bytes(freed)}." + (failed > 0 ? $" {failed:N0} files were in use and skipped." : "");
        if (skipped.Count > 0) Status += $" Close {string.Join(", ", skipped)} to clean them.";
        State = CleanupState.Cleaned;
        OnPropertyChanged(nameof(SelectedFiles));
        Notifier.Show("Cleanup complete", $"Freed {Fmt.Bytes(freed)} of disk space.", ToastKind.Success);
    }
}
