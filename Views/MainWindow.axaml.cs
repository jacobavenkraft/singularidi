using Avalonia.Controls;
using Singularidi.ViewModels;

namespace Singularidi.Views;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }

    public MainWindow(MainWindowViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.ExitRequested += Close;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ExitRequested -= Close;
            vm.Dispose();
        }
        base.OnClosed(e);
    }
}
