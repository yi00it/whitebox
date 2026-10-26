using System.Collections.ObjectModel;
using Whitebox.Core;
using Whitebox.Services;

namespace Whitebox.ViewModels;

public sealed class StartupViewModel : ObservableObject
{
    private bool _loaded;

    public StartupViewModel()
    {
        RefreshCommand = new AsyncCommand(LoadAsync);
        RevealCommand = new RelayCommand(p => { if (p is StartupItem { ExePath: { } path }) Shell.Reveal(path); });
        OpenTaskManagerCommand = new RelayCommand(() => Shell.Open("taskmgr.exe"));
        StartupService.Changed += () => OnPropertyChanged(nameof(Summary));
    }

    public AsyncCommand RefreshCommand { get; }
    public RelayCommand RevealCommand { get; }
    public RelayCommand OpenTaskManagerCommand { get; }

    private ObservableCollection<StartupItem> _items = [];
    public ObservableCollection<StartupItem> Items { get => _items; private set => Set(ref _items, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    public string Summary
    {
        get
        {
            int on = Items.Count(i => i.IsEnabled);
            return Items.Count == 0 ? "No startup apps found" : $"{on} of {Items.Count} apps launch when you sign in";
        }
    }

    public bool IsAdmin => AdminHelper.IsAdmin;

    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        RefreshCommand.Execute(null);
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        var items = await Task.Run(StartupService.GetItems);
        Items = new ObservableCollection<StartupItem>(items);
        foreach (var item in items)
        {
            var it = item;
            IconLoader.Load(it.ExePath, img => it.Icon = img);
        }
        IsLoading = false;
        OnPropertyChanged(nameof(Summary));
    }
}
