using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Singularidi.Config;
using Singularidi.Midi;
using Singularidi.Themes;
using Singularidi.ViewModels;

namespace Singularidi.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly MidiPlaybackEngine _engine;

    public MainWindow(MainWindowViewModel vm, MidiPlaybackEngine engine, AppConfig config)
    {
        InitializeComponent();
        _vm = vm;
        _engine = engine;
        DataContext = _vm;

        Visualizer.SetEngine(_engine);
        Visualizer.HighlightActiveNotes = config.HighlightActiveNotes;
        MenuHighlightNotes.IsChecked = config.HighlightActiveNotes;

        // Apply saved theme
        ApplyTheme(config.ThemeName);
        RefreshThemeMenu();
        RefreshMidiDevicesMenu();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.MidiDevices))
                RefreshMidiDevicesMenu();
        };
    }

    // ── Theme menu ───────────────────────────────────────────────────────

    private void RefreshThemeMenu()
    {
        MenuTheme.Items.Clear();
        foreach (var name in _vm.AvailableThemes)
        {
            var item = new MenuItem { Header = name };
            var themeName = name; // capture for closure
            item.Click += (_, _) => ApplyTheme(themeName);
            MenuTheme.Items.Add(item);
        }
        MenuTheme.Items.Add(new Separator());
        var createItem = new MenuItem { Header = "Create Custom Theme…" };
        createItem.Click += OnCreateCustomTheme;
        MenuTheme.Items.Add(createItem);
    }

    private void ApplyTheme(string name)
    {
        _vm.SetTheme(name);
        Visualizer.Theme = _vm.GetTheme(name);
    }

    private async void OnCreateCustomTheme(object? sender, RoutedEventArgs e)
    {
        // Clone current theme as starting point
        var currentTheme = _vm.GetTheme(_vm.ThemeName);
        ThemeData startingData;
        if (currentTheme is ThemeData td)
            startingData = td.Clone();
        else
            startingData = BuiltInThemes.Dark(); // fallback

        startingData.Name = "My Custom Theme";

        var editor = new ThemeEditorWindow(startingData);
        var result = await editor.ShowDialog<ThemeData?>(this);
        if (result != null)
        {
            _vm.SaveCustomTheme(result);
            Visualizer.Theme = result;
            RefreshThemeMenu();
        }
    }

    // ── MIDI device menu ─────────────────────────────────────────────────

    private void RefreshMidiDevicesMenu()
    {
        _vm.RefreshMidiDevices();
        MenuMidiDevices.Items.Clear();
        foreach (var device in _vm.MidiDevices)
        {
            var item = new MenuItem { Header = device };
            item.Click += (_, _) => _vm.SelectMidiDevice(device);
            MenuMidiDevices.Items.Add(item);
        }
        if (MenuMidiDevices.Items.Count == 0)
        {
            MenuMidiDevices.Items.Add(new MenuItem { Header = "(no MIDI devices found)", IsEnabled = false });
        }
    }

    // ── File menu ──────────────────────────────────────────────────────────

    private async void OnOpenMidiFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MIDI File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MIDI Files") { Patterns = new[] { "*.mid", "*.midi" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });
        if (files.Count > 0)
            _vm.OnMidiFileOpened(files[0].Path.LocalPath);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    // ── View menu ──────────────────────────────────────────────────────────

    private void OnToggleHighlightNotes(object? sender, RoutedEventArgs e)
    {
        bool value = MenuHighlightNotes.IsChecked;
        Visualizer.HighlightActiveNotes = value;
        _vm.SetHighlightActiveNotes(value);
    }

    // ── Audio menu ─────────────────────────────────────────────────────────

    private void OnSetSoundFont(object? sender, RoutedEventArgs e)
        => _vm.SetOutputMode(AudioOutputMode.SoundFont);

    private void OnSetMidiDevice(object? sender, RoutedEventArgs e)
        => _vm.SetOutputMode(AudioOutputMode.MidiDevice);

    private async void OnSelectSoundFont(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select SoundFont",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SoundFont Files") { Patterns = new[] { "*.sf2" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });
        if (files.Count > 0)
            _vm.SetSoundFontPath(files[0].Path.LocalPath);
    }

    private void OnSaveSoundFontDefault(object? sender, RoutedEventArgs e)
        => _vm.SaveSoundFontAsDefault();

    protected override void OnClosed(EventArgs e)
    {
        _engine.Dispose();
        base.OnClosed(e);
    }
}
