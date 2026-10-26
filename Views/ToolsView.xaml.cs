using System.Windows.Controls;
using Whitebox.ViewModels;

namespace Whitebox.Views;

public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as ToolsViewModel)?.EnsureLoaded();
    }
}
