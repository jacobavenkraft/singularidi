using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Singularidi.Audio;
using Singularidi.Config;
using Singularidi.Export;
using Singularidi.Midi;
using Singularidi.Services;
using Singularidi.Themes;
using Singularidi.Visualization;

namespace Singularidi.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly MidiPlaybackEngine _engine;
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
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

    [ObservableProperty]
    private bool _highlightActiveNotes = true;

    [ObservableProperty]
    private IVisualTheme _currentTheme = BuiltInThemes.Dark();

    [ObservableProperty]
    private IVisualizationEngine _currentVisualization = new VerticalFallEngine();

    public MidiPlaybackEngine Engine => _engine;

    private readonly List<IVisualizationEngine> _availableVisualizations =
    [
        new VerticalFallEngine(),
        new HorizontalCrawlEngine(),
    ];

    public ObservableCollection<MenuItemViewModel> ThemeMenuItems { get; } = new();
    public ObservableCollection<MenuItemViewModel> MidiDeviceMenuItems { get; } = new();
    public ObservableCollection<MenuItemViewModel> VisualizationMenuItems { get; } = new();
    public ObservableCollection<MenuItemViewModel> GuideLineStyleMenuItems { get; } = new();

    // ── Constructor ────────────────────────────────────────────────────────

    public MainWindowViewModel(
        MidiPlaybackEngine engine,
        IConfigService configService,
        IDialogService dialogService,
        AppConfig config)
    {
        _engine = engine;
        _configService = configService;
        _dialogService = dialogService;
        _config = config;
        _currentOutputMode = config.OutputMode;
        _soundFontPath = config.SoundFontPath;
        _selectedMidiDevice = config.PreferredMidiDevice;
        _themeName = config.ThemeName;
        _highlightActiveNotes = config.HighlightActiveNotes;
        _themeRegistry = new ThemeRegistry(config.CustomThemes);
        _currentTheme = _themeRegistry.Get(config.ThemeName);

        RefreshMidiDeviceMenuItems();
        RefreshThemeMenuItems();
        RefreshVisualizationMenuItems();
        RefreshGuideLineStyleMenuItems();
        RebuildAudioEngine();

        // Restore last-used visualization
        var savedViz = _availableVisualizations.FirstOrDefault(v => v.Name == config.VisualizationType);
        if (savedViz != null)
            CurrentVisualization = savedViz;

        // Restore last-used guide line style
        if (!string.IsNullOrEmpty(config.GuideLineStyle) &&
            Enum.TryParse<GuideLineStyle>(config.GuideLineStyle, out var savedStyle))
            ApplyGuideLineStyle(savedStyle);

        if (string.IsNullOrEmpty(config.SoundFontPath) && config.OutputMode == AudioOutputMode.SoundFont)
            StatusText = "No SoundFont configured — use Audio menu to select one";

        if (!string.IsNullOrEmpty(config.LastMidiFilePath) && File.Exists(config.LastMidiFilePath))
            OnMidiFileOpened(config.LastMidiFilePath);
    }

    // ── Highlight toggle ───────────────────────────────────────────────────

    partial void OnHighlightActiveNotesChanged(bool value)
    {
        _config.HighlightActiveNotes = value;
        _configService.Save(_config);
    }

    // ── File commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenMidiFileAsync()
    {
        var path = await _dialogService.OpenMidiFileAsync();
        if (path != null)
            OnMidiFileOpened(path);
    }

    public void OnMidiFileOpened(string path)
    {
        try
        {
            _engine.Load(path);
            _config.LastMidiFilePath = path;
            _configService.Save(_config);
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
    private void Exit()
    {
        // The view will handle closing by binding to this command
        ExitRequested?.Invoke();
    }

    public event Action? ExitRequested;

    // Expose for export progress dialog ownership
    public event Func<ExportProgressViewModel, Task>? ShowExportProgress;

    [RelayCommand]
    private async Task ExportToMp4Async()
    {
        if (string.IsNullOrEmpty(_config.LastMidiFilePath) || !File.Exists(_config.LastMidiFilePath))
        {
            StatusText = "No MIDI file loaded — open a file first.";
            return;
        }

        if (string.IsNullOrEmpty(_config.SoundFontPath) || !File.Exists(_config.SoundFontPath))
        {
            StatusText = "No SoundFont configured — select one in Audio menu first.";
            return;
        }

        if (!Mp4Exporter.IsFfmpegAvailable(_config.FfmpegPath))
        {
            StatusText = "FFmpeg not found — install FFmpeg and ensure it is in PATH.";
            return;
        }

        var outputPath = await _dialogService.SaveMp4FileAsync();
        if (outputPath == null) return;

        var progressVm = new ExportProgressViewModel();
        var exportSettings = new ExportSettings(
            _config.ExportWidth, _config.ExportHeight, _config.ExportFps, _config.FfmpegPath);

        // Show progress window (the view handles this via event)
        _ = ShowExportProgress?.Invoke(progressVm);

        try
        {
            var exporter = new Mp4Exporter();
            var progressReporter = new Progress<(double progress, string status)>(
                update => progressVm.Update(update.progress, update.status));

            await exporter.ExportAsync(
                _config.LastMidiFilePath,
                _config.SoundFontPath,
                outputPath,
                exportSettings,
                CurrentVisualization,
                _engine.Notes,
                _engine.TotalDurationSeconds,
                CurrentTheme,
                HighlightActiveNotes,
                progressReporter,
                progressVm.Cts.Token);

            progressVm.Complete($"Export complete: {Path.GetFileName(outputPath)}");
            StatusText = $"Exported: {Path.GetFileName(outputPath)}";
        }
        catch (OperationCanceledException)
        {
            progressVm.Complete("Export cancelled.");
            StatusText = "Export cancelled.";
            try { File.Delete(outputPath); } catch { }
        }
        catch (Exception ex)
        {
            progressVm.Complete($"Export failed: {ex.Message}");
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    // ── Playback commands ──────────────────────────────────────────────────

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

    // ── Audio commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void SetSoundFontMode() => SetOutputMode(AudioOutputMode.SoundFont);

    [RelayCommand]
    private void SetMidiDeviceMode() => SetOutputMode(AudioOutputMode.MidiDevice);

    [RelayCommand]
    private async Task SelectSoundFontAsync()
    {
        var path = await _dialogService.OpenSoundFontAsync();
        if (path != null)
            SetSoundFontPath(path);
    }

    [RelayCommand]
    private void SaveSoundFontDefault()
    {
        _config.SoundFontPath = SoundFontPath;
        _configService.Save(_config);
        StatusText = "SoundFont saved as default.";
    }

    private void SetSoundFontPath(string path)
    {
        SoundFontPath = path;
        _config.SoundFontPath = path;
        RebuildAudioEngine();
        StatusText = $"SoundFont: {Path.GetFileName(path)}";
    }

    private void SetOutputMode(AudioOutputMode mode)
    {
        CurrentOutputMode = mode;
        _config.OutputMode = mode;
        RebuildAudioEngine();
    }

    [RelayCommand]
    private void SelectMidiDevice(string name)
    {
        SelectedMidiDevice = name;
        _config.PreferredMidiDevice = name;
        RebuildAudioEngine();
    }

    // ── Theme commands ─────────────────────────────────────────────────────

    [RelayCommand]
    private void ApplyTheme(string name)
    {
        ThemeName = name;
        _config.ThemeName = name;
        _configService.Save(_config);
        CurrentTheme = _themeRegistry.Get(name);
    }

    [RelayCommand]
    private async Task CreateCustomThemeAsync()
    {
        var currentTheme = _themeRegistry.Get(ThemeName);
        ThemeData startingData;
        if (currentTheme is ThemeData td)
            startingData = td.Clone();
        else
            startingData = BuiltInThemes.Dark();

        startingData.Name = "My Custom Theme";

        var result = await _dialogService.ShowThemeEditorAsync(startingData);
        if (result != null)
        {
            SaveCustomTheme(result);
            RefreshThemeMenuItems();
        }
    }

    private void SaveCustomTheme(ThemeData theme)
    {
        _themeRegistry.AddOrUpdate(theme);
        _config.CustomThemes ??= new List<ThemeData>();

        var existing = _config.CustomThemes.FindIndex(t => t.Name == theme.Name);
        if (existing >= 0)
            _config.CustomThemes[existing] = theme;
        else
            _config.CustomThemes.Add(theme);

        ApplyTheme(theme.Name);
    }

    // ── Visualization commands ──────────────────────────────────────────────

    [RelayCommand]
    private void SetVisualization(string name)
    {
        var viz = _availableVisualizations.FirstOrDefault(v => v.Name == name);
        if (viz != null)
        {
            CurrentVisualization = viz;
            _config.VisualizationType = name;
            _configService.Save(_config);
        }
    }

    [RelayCommand]
    private void SetGuideLineStyle(string styleName)
    {
        if (Enum.TryParse<GuideLineStyle>(styleName, out var style))
        {
            ApplyGuideLineStyle(style);
            _config.GuideLineStyle = styleName;
            _configService.Save(_config);
        }
    }

    private void ApplyGuideLineStyle(GuideLineStyle style)
    {
        foreach (var viz in _availableVisualizations)
        {
            if (viz is VerticalFallEngine vfe) vfe.GuideLineStyle = style;
            else if (viz is HorizontalCrawlEngine hce) hce.GuideLineStyle = style;
        }
    }

    public void RegisterVisualization(IVisualizationEngine engine)
    {
        if (_availableVisualizations.All(v => v.Name != engine.Name))
        {
            _availableVisualizations.Add(engine);
            RefreshVisualizationMenuItems();
        }
    }

    // ── Dynamic menu builders ──────────────────────────────────────────────

    private void RefreshThemeMenuItems()
    {
        ThemeMenuItems.Clear();
        foreach (var name in _themeRegistry.AvailableThemes)
        {
            var themeName = name; // capture
            ThemeMenuItems.Add(new MenuItemViewModel(themeName, new RelayCommand(() => ApplyTheme(themeName))));
        }
        ThemeMenuItems.Add(new MenuItemViewModel("Create Custom Theme…", CreateCustomThemeCommand));
    }

    private void RefreshVisualizationMenuItems()
    {
        VisualizationMenuItems.Clear();
        foreach (var viz in _availableVisualizations)
        {
            var vizName = viz.Name; // capture
            VisualizationMenuItems.Add(new MenuItemViewModel(vizName, new RelayCommand(() => SetVisualization(vizName))));
        }
    }

    private void RefreshGuideLineStyleMenuItems()
    {
        GuideLineStyleMenuItems.Clear();
        foreach (var style in Enum.GetValues<GuideLineStyle>())
        {
            var name = style.ToString();
            // Add spaces before capitals for display: "KeyWidthCentered" → "Key Width Centered"
            var display = System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1");
            GuideLineStyleMenuItems.Add(new MenuItemViewModel(display, new RelayCommand(() => SetGuideLineStyle(name))));
        }
    }

    private void RefreshMidiDeviceMenuItems()
    {
        MidiDeviceMenuItems.Clear();
        var devices = MidiDeviceAudioEngine.GetAvailableDevices();
        if (devices.Count == 0)
        {
            MidiDeviceMenuItems.Add(new MenuItemViewModel { Header = "(no MIDI devices found)", IsEnabled = false });
            return;
        }
        foreach (var device in devices)
        {
            var deviceName = device; // capture
            MidiDeviceMenuItems.Add(new MenuItemViewModel(deviceName, new RelayCommand(() => SelectMidiDevice(deviceName))));
        }
    }

    // ── Audio engine ───────────────────────────────────────────────────────

    private void RebuildAudioEngine()
    {
        IAudioEngine engine = CurrentOutputMode == AudioOutputMode.SoundFont
            ? new SoundFontAudioEngine(SoundFontPath)
            : new MidiDeviceAudioEngine(SelectedMidiDevice);
        _engine.SetAudioEngine(engine);
    }

    public void Dispose()
    {
        _engine.Dispose();
    }
}
