using Singularidi.Themes;

namespace Singularidi.Config;

public class AppConfig
{
    public string SoundFontPath { get; set; } = "";
    public AudioOutputMode OutputMode { get; set; } = AudioOutputMode.SoundFont;
    public string PreferredMidiDevice { get; set; } = "";
    public bool HighlightActiveNotes { get; set; } = true;
    public string LastMidiFilePath { get; set; } = "";
    public string ThemeName { get; set; } = "Dark";
    public string VisualizationType { get; set; } = "Vertical Fall";
    public List<ThemeData>? CustomThemes { get; set; }

    // Export settings
    public string? FfmpegPath { get; set; }
    public int ExportWidth { get; set; } = 1920;
    public int ExportHeight { get; set; } = 1080;
    public int ExportFps { get; set; } = 60;
}

public enum AudioOutputMode
{
    SoundFont,
    MidiDevice
}
