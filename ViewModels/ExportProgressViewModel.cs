using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Singularidi.ViewModels;

public sealed partial class ExportProgressViewModel : ObservableObject
{
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "Preparing export...";

    [ObservableProperty]
    private bool _isComplete;

    public CancellationTokenSource Cts { get; } = new();

    [RelayCommand]
    private void Cancel()
    {
        Cts.Cancel();
        StatusText = "Cancelling...";
    }

    public void Update(double progress, string status)
    {
        Progress = progress;
        StatusText = status;
    }

    public void Complete(string message)
    {
        Progress = 1.0;
        StatusText = message;
        IsComplete = true;
    }
}
