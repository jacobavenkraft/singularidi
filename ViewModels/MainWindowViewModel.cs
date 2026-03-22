using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Singularidi.Audio;
using Singularidi.Config;
using Singularidi.Midi;

namespace Singularidi.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly MidiPlaybackEngine _engine;
    private AppConfig _config;

    // ── Observable properties ──────────────────────────────────────────────

    private string _statusText = "No file loaded";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _canPlay;
    public bool CanPlay { get => _canPlay; set { _canPlay = value; OnPropertyChanged(); } }

    private bool _canPause;
    public bool CanPause { get => _canPause; set { _canPause = value; OnPropertyChanged(); } }

    private bool _canStop;
    public bool CanStop { get => _canStop; set { _canStop = value; OnPropertyChanged(); } }

    private AudioOutputMode _currentOutputMode;
    public AudioOutputMode CurrentOutputMode
    {
        get => _currentOutputMode;
        set { _currentOutputMode = value; OnPropertyChanged(); }
    }

    private string _soundFontPath = "";
    public string SoundFontPath
    {
        get => _soundFontPath;
        set { _soundFontPath = value; OnPropertyChanged(); }
    }

    private string _selectedMidiDevice = "";
    public string SelectedMidiDevice
    {
        get => _selectedMidiDevice;
        set { _selectedMidiDevice = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> MidiDevices { get; } = new();

    // ── Constructor ────────────────────────────────────────────────────────

    public MainWindowViewModel(MidiPlaybackEngine engine, AppConfig config)
    {
        _engine = engine;
        _config = config;
        _currentOutputMode = config.OutputMode;
        _soundFontPath = config.SoundFontPath;
        _selectedMidiDevice = config.PreferredMidiDevice;

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

    public void Play()
    {
        _engine.Play();
        StatusText = "Playing…";
        CanPlay = false;
        CanPause = true;
        CanStop = true;
    }

    public void Pause()
    {
        _engine.Pause();
        StatusText = "Paused";
        CanPlay = true;
        CanPause = false;
        CanStop = true;
    }

    public void Stop()
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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
