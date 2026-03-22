using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Singularidi.Audio;
using Singularidi.Config;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly MidiPlaybackEngine _engine;
    private AppConfig _config;
    private readonly ThemeRegistry _themeRegistry;

    // ── Observable properties ──────────────────────────────────────────────

    [ObservableProperty]
    private string _statusText = "No file loaded";

    [ObservableProperty]
    private bool _canPlay;

    [ObservableProperty]
    private bool _canPause;

    [ObservableProperty]
    private bool _canStop;

    [ObservableProperty]
    private AudioOutputMode _currentOutputMode;

    [ObservableProperty]
    private string _soundFontPath = "";

    [ObservableProperty]
    private string _selectedMidiDevice = "";

    [ObservableProperty]
    private string _themeName = "Dark";

    public ObservableCollection<string> MidiDevices { get; } = new();

    public IReadOnlyCollection<string> AvailableThemes => _themeRegistry.AvailableThemes;

    // ── Constructor ────────────────────────────────────────────────────────

    public MainWindowViewModel(MidiPlaybackEngine engine, AppConfig config)
    {
        _engine = engine;
        _config = config;
        _currentOutputMode = config.OutputMode;
        _soundFontPath = config.SoundFontPath;
        _selectedMidiDevice = config.PreferredMidiDevice;
        _themeName = config.ThemeName;
        _themeRegistry = new ThemeRegistry(config.CustomThemes);

        RefreshMidiDevices();
        RebuildAudioEngine();

        if (string.IsNullOrEmpty(config.SoundFontPath) && config.OutputMode == AudioOutputMode.SoundFont)
            StatusText = "No SoundFont configured — use Audio menu to select one";

        if (!string.IsNullOrEmpty(config.LastMidiFilePath) && File.Exists(config.LastMidiFilePath))
            OnMidiFileOpened(config.LastMidiFilePath);
    }

    // ── Commands ───────────────────────────────────────────────────────────

    public void OnMidiFileOpened(string path)
    {
        try
        {
            _engine.Load(path);
            _config.LastMidiFilePath = path;
            ConfigService.Save(_config);
            StatusText = $"Loaded: {Path.GetFileName(path)}";
            CanPlay = true;
            CanPause = false;
            CanStop = false;
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading file: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Play()
    {
        _engine.Play();
        StatusText = "Playing…";
        CanPlay = false;
        CanPause = true;
        CanStop = true;
    }

    [RelayCommand]
    private void Pause()
    {
        _engine.Pause();
        StatusText = "Paused";
        CanPlay = true;
        CanPause = false;
        CanStop = true;
    }

    [RelayCommand]
    private void Stop()
    {
        _engine.Stop();
        StatusText = "Stopped";
        CanPlay = true;
        CanPause = false;
        CanStop = false;
    }

    public void SetSoundFontPath(string path)
    {
        SoundFontPath = path;
        _config.SoundFontPath = path;
        RebuildAudioEngine();
        StatusText = $"SoundFont: {Path.GetFileName(path)}";
    }

    public void SaveSoundFontAsDefault()
    {
        _config.SoundFontPath = SoundFontPath;
        ConfigService.Save(_config);
        StatusText = "SoundFont saved as default.";
    }

    public void SetOutputMode(AudioOutputMode mode)
    {
        CurrentOutputMode = mode;
        _config.OutputMode = mode;
        RebuildAudioEngine();
    }

    public void SelectMidiDevice(string name)
    {
        SelectedMidiDevice = name;
        _config.PreferredMidiDevice = name;
        RebuildAudioEngine();
    }

    public void SetHighlightActiveNotes(bool value)
    {
        _config.HighlightActiveNotes = value;
        ConfigService.Save(_config);
    }

    public void SetTheme(string themeName)
    {
        ThemeName = themeName;
        _config.ThemeName = themeName;
        ConfigService.Save(_config);
    }

    public IVisualTheme GetTheme(string name) => _themeRegistry.Get(name);

    public void SaveCustomTheme(ThemeData theme)
    {
        _themeRegistry.AddOrUpdate(theme);
        _config.CustomThemes ??= new List<ThemeData>();

        var existing = _config.CustomThemes.FindIndex(t => t.Name == theme.Name);
        if (existing >= 0)
            _config.CustomThemes[existing] = theme;
        else
            _config.CustomThemes.Add(theme);

        SetTheme(theme.Name);
    }

    public void RefreshMidiDevices()
    {
        MidiDevices.Clear();
        foreach (var d in MidiDeviceAudioEngine.GetAvailableDevices())
            MidiDevices.Add(d);
    }

    private void RebuildAudioEngine()
    {
        IAudioEngine engine = CurrentOutputMode == AudioOutputMode.SoundFont
            ? new SoundFontAudioEngine(SoundFontPath)
            : new MidiDeviceAudioEngine(SelectedMidiDevice);
        _engine.SetAudioEngine(engine);
    }
}
