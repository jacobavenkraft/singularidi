namespace Singularidi.Config;

public class AppConfig
{
    public string SoundFontPath { get; set; } = "";
    public AudioOutputMode OutputMode { get; set; } = AudioOutputMode.SoundFont;
    public string PreferredMidiDevice { get; set; } = "";
    public bool HighlightActiveNotes { get; set; } = true;
    public string LastMidiFilePath { get; set; } = "";
}

public enum AudioOutputMode
{
    SoundFont,
    MidiDevice
}
