using System.Windows.Controls;
using Whitebox.ViewModels;

namespace Whitebox.Views;

public partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as StartupViewModel)?.EnsureLoaded();
    }
}
