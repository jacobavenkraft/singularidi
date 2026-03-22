namespace Singularidi.Themes;

public static class BuiltInThemes
{
    public static ThemeData Dark() => new()
    {
        Name = "Dark",
        Background = "#0D0D0D",
        GuideLine = "#19FFFFFF",
        NoteShape = NoteShape.Rectangular,
        ChannelColorValues =
        [
            "#FF5050", "#FF9040", "#FFD740", "#70E050",
            "#40D4D4", "#4080FF", "#A050FF", "#FF50C8",
            "#FF8080", "#80FF80", "#80FFFF", "#8080FF",
            "#FF80C0", "#FFC080", "#C0FF80", "#C080FF",
        ],
        WhiteKey = "#1A1A1A",   // reversed: whole-note keys are dark
        BlackKey = "#F0F0F0",   // reversed: sharp/flat keys are light
        ActiveHighlight = "#FFFFFF",
        ActiveNoteBlend = 0.4f,
        ActiveWhiteKeyBlend = 0.5f,
        ActiveBlackKeyBlend = 0.3f,
    };

    public static ThemeData Light() => new()
    {
        Name = "Light",
        Background = "#F5F5F5",
        GuideLine = "#1E000000",
        NoteShape = NoteShape.Rectangular,
        ChannelColorValues =
        [
            "#FF9999", "#FFBB88", "#FFE088", "#AAEE99",
            "#88DDDD", "#99BBFF", "#CC99FF", "#FF99DD",
            "#FFAAAA", "#AAFFAA", "#AAFFFF", "#AAAAFF",
            "#FFAAD4", "#FFDDAA", "#DDFFAA", "#DDAAFF",
        ],
        WhiteKey = "#F0F0F0",   // normal: white keys
        BlackKey = "#1A1A1A",   // normal: black keys
        ActiveHighlight = "#000000",
        ActiveNoteBlend = 0.3f,
        ActiveWhiteKeyBlend = 0.4f,
        ActiveBlackKeyBlend = 0.2f,
    };
}
