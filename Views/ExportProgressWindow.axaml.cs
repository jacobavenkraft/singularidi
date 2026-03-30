using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Singularidi.Views;

public partial class ExportProgressWindow : Window
{
    public ExportProgressWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
