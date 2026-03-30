namespace Singularidi.Export;

public record ExportSettings(
    int Width = 1920,
    int Height = 1080,
    int Fps = 60,
    string? FfmpegPath = null); // null = use PATH
