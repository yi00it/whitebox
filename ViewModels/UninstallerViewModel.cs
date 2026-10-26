using System.Collections.ObjectModel;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class UninstallerViewModel : ObservableObject
{
    private List<InstalledApp> _all = [];
    private bool _loaded;

    public UninstallerViewModel()
    {
        RefreshCommand = new AsyncCommand(LoadAsync);
        UninstallCommand = new AsyncCommand(p => UninstallAsync(p as InstalledApp), p => p is InstalledApp { IsBusy: false });
        RevealCommand = new RelayCommand(p =>
        {
            if (p is InstalledApp { InstallLocation: { } loc } && Directory.Exists(loc)) Shell.Open(loc);
            else Notifier.Show("No install folder", "This app didn't register its install location.", ToastKind.Info);
        });
        OpenSettingsCommand = new RelayCommand(() => Shell.Open("ms-settings:appsfeatures"));
    }

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand UninstallCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }

    public string[] SortOptions { get; } = ["Name", "Size", "Recent"];

    private string _sortBy = "Name";
    public string SortBy { get => _sortBy; set { if (Set(ref _sortBy, value)) ApplyView(); } }

    private string _filter = "";
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) ApplyView(); } }

    private ObservableCollection<InstalledApp> _apps = [];
    public ObservableCollection<InstalledApp> Apps { get => _apps; private set => Set(ref _apps, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    private string _summary = "";
    public string Summary { get => _summary; set => Set(ref _summary, value); }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        RefreshCommand.Execute(null);
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        _all = await Task.Run(AppsService.GetInstalled);
        foreach (var app in _all)
        {
            var a = app;
            IconLoader.Load(a.IconPath, img => a.Icon = img);
        }
        Summary = $"{_all.Count} apps  ·  {Fmt.Bytes(_all.Sum(a => a.Size))} reported";
        ApplyView();
        IsLoading = false;
    }

    private void ApplyView()
    {
        IEnumerable<InstalledApp> q = _all;
        if (!string.IsNullOrWhiteSpace(Filter))
            q = q.Where(a => a.Name.Contains(Filter, StringComparison.CurrentCultureIgnoreCase)
                             || (a.Publisher?.Contains(Filter, StringComparison.CurrentCultureIgnoreCase) ?? false));
        q = SortBy switch
        {
            "Size" => q.OrderByDescending(a => a.Size),
            "Recent" => q.OrderByDescending(a => a.InstallDate ?? DateTime.MinValue),
            _ => q.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        Apps = new ObservableCollection<InstalledApp>(q);
    }

    private async Task UninstallAsync(InstalledApp? app)
    {
        if (app == null) return;
        if (!Dialogs.Confirm($"Uninstall {app.Name}?",
                "The app's own uninstaller will open. Follow its steps to finish removing it.", "Uninstall", true))
            return;

        app.IsBusy = true;
        try
        {
            await AppsService.UninstallAsync(app);
        }
        catch (Exception ex)
        {
            Notifier.Show("Couldn't start uninstaller", ex.Message, ToastKind.Error);
            app.IsBusy = false;
            return;
        }

        await Task.Delay(1500);
        bool still = await Task.Run(() => AppsService.StillInstalled(app));
        app.IsBusy = false;
        if (still)
        {
            Notifier.Show("Uninstall not completed", $"{app.Name} is still installed.", ToastKind.Info);
            return;
        }

        _all.Remove(app);
        ApplyView();
        Notifier.Show("Uninstalled", app.Name, ToastKind.Success);

        // Offer to clear leftovers in the install folder.
        var loc = app.InstallLocation;
        if (loc != null && Directory.Exists(loc) && loc.Length > 10 && !IsProtectedFolder(loc))
        {
            long size = await Task.Run(() => FileScanner.Enumerate(loc, 0, CancellationToken.None).Sum(f => f.Size));
            if (Dialogs.Confirm("Remove leftovers?",
                    $"{app.Name} left {Fmt.Bytes(size)} behind in\n{loc}\n\nMove this folder to the Recycle Bin?", "Move to Recycle Bin"))
            {
                await Task.Run(() => Shell.Recycle([loc]));
                Notifier.Show("Leftovers removed", Fmt.Bytes(size) + " moved to the Recycle Bin.", ToastKind.Success);
            }
        }
    }

    private static bool IsProtectedFolder(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\');
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetPathRoot(full) ?? "",
        ];
        return roots.Any(r => string.Equals(r.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase));
    }
}
