using System.Windows.Controls;
using Whitebox.ViewModels;

namespace Whitebox.Views;

public partial class UninstallerView : UserControl
{
    public UninstallerView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as UninstallerViewModel)?.EnsureLoaded();
    }
}
