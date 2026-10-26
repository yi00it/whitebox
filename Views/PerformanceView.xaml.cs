using System.Windows.Controls;
using Whitebox.ViewModels;

namespace Whitebox.Views;

public partial class PerformanceView : UserControl
{
    public PerformanceView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) => (DataContext as PerformanceViewModel)?.SetActive((bool)e.NewValue);
    }
}
