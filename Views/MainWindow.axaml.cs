using Avalonia.Controls;
using Singularidi.Visualization;
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
        vm.ShowExportProgress += OnShowExportProgress;
        vm.GlOverlayChanged += OnGlOverlayChanged;
    }

    private void OnGlOverlayChanged(PianoGlControl? glControl)
    {
        Visualizer.SetGlOverlay(glControl);
    }

    private async Task OnShowExportProgress(ExportProgressViewModel progressVm)
    {
        var window = new ExportProgressWindow { DataContext = progressVm };
        await window.ShowDialog(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ExitRequested -= Close;
            vm.ShowExportProgress -= OnShowExportProgress;
            vm.GlOverlayChanged -= OnGlOverlayChanged;
            vm.Dispose();
        }
        base.OnClosed(e);
    }
}
