using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Singularidi.ViewModels;

public sealed partial class MenuItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _header = "";

    [ObservableProperty]
    private ICommand? _command;

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isEnabled = true;

    public ObservableCollection<MenuItemViewModel>? Items { get; init; }

    public MenuItemViewModel() { }

    public MenuItemViewModel(string header, ICommand command)
    {
        _header = header;
        _command = command;
    }
}
