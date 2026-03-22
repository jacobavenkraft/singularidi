using System.Text.Json.Serialization;
using Avalonia.Media;

namespace Singularidi.Themes;

/// <summary>
/// Serializable implementation of <see cref="IVisualTheme"/>.
/// Color values are stored as hex strings for human-readable JSON.
/// </summary>
public class ThemeData : IVisualTheme
{
    public string Name { get; set; } = "Custom";

    public string Background { get; set; } = "#0D0D0D";
    public string GuideLine { get; set; } = "#19FFFFFF";
    public NoteShape NoteShape { get; set; } = NoteShape.Rectangular;

    public string[] ChannelColorValues { get; set; } =
    [
        "#FF5050", "#FF9040", "#FFD740", "#70E050",
        "#40D4D4", "#4080FF", "#A050FF", "#FF50C8",
        "#FF8080", "#80FF80", "#80FFFF", "#8080FF",
        "#FF80C0", "#FFC080", "#C0FF80", "#C080FF",
    ];

    public Dictionary<int, string>? NoteColorOverrideValues { get; set; }

    public string WhiteKey { get; set; } = "#F0F0F0";
    public string BlackKey { get; set; } = "#1A1A1A";
    public Dictionary<int, string>? KeyColorOverrideValues { get; set; }

    public string ActiveHighlight { get; set; } = "#FFFFFF";
    public float ActiveNoteBlend { get; set; } = 0.4f;
    public float ActiveWhiteKeyBlend { get; set; } = 0.5f;
    public float ActiveBlackKeyBlend { get; set; } = 0.3f;

    // ── IVisualTheme computed properties ────────────────────────────────

    [JsonIgnore] public Color BackgroundColor => Color.Parse(Background);
    [JsonIgnore] public Color GuideLineColor => Color.Parse(GuideLine);

    [JsonIgnore]
    public Color[] ChannelColors =>
        ChannelColorValues.Select(Color.Parse).ToArray();

    [JsonIgnore]
    public Dictionary<int, Color>? NoteColorOverrides =>
        NoteColorOverrideValues?.ToDictionary(kv => kv.Key, kv => Color.Parse(kv.Value));

    [JsonIgnore] public Color WhiteKeyColor => Color.Parse(WhiteKey);
    [JsonIgnore] public Color BlackKeyColor => Color.Parse(BlackKey);

    [JsonIgnore]
    public Dictionary<int, Color>? KeyColorOverrides =>
        KeyColorOverrideValues?.ToDictionary(kv => kv.Key, kv => Color.Parse(kv.Value));

    [JsonIgnore] public Color ActiveHighlightColor => Color.Parse(ActiveHighlight);

    // ── Clone ───────────────────────────────────────────────────────────

    public ThemeData Clone() => new()
    {
        Name = Name,
        Background = Background,
        GuideLine = GuideLine,
        NoteShape = NoteShape,
        ChannelColorValues = (string[])ChannelColorValues.Clone(),
        NoteColorOverrideValues = NoteColorOverrideValues != null
            ? new Dictionary<int, string>(NoteColorOverrideValues)
            : null,
        WhiteKey = WhiteKey,
        BlackKey = BlackKey,
        KeyColorOverrideValues = KeyColorOverrideValues != null
            ? new Dictionary<int, string>(KeyColorOverrideValues)
            : null,
        ActiveHighlight = ActiveHighlight,
        ActiveNoteBlend = ActiveNoteBlend,
        ActiveWhiteKeyBlend = ActiveWhiteKeyBlend,
        ActiveBlackKeyBlend = ActiveBlackKeyBlend,
    };
}
