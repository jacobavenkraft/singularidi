using Avalonia.Media;

namespace Singularidi.Themes;

public interface IVisualTheme
{
    string Name { get; }

    Color BackgroundColor { get; }
    Color GuideLineColor { get; }

    NoteShape NoteShape { get; }

    /// <summary>16 colors, one per MIDI channel (0–15).</summary>
    Color[] ChannelColors { get; }

    /// <summary>Sparse per-note overrides keyed by MIDI note number (0–127). Null = use ChannelColors.</summary>
    Dictionary<int, Color>? NoteColorOverrides { get; }

    Color WhiteKeyColor { get; }
    Color BlackKeyColor { get; }

    /// <summary>Sparse per-key overrides keyed by MIDI note number (0–127). Null = use WhiteKeyColor/BlackKeyColor.</summary>
    Dictionary<int, Color>? KeyColorOverrides { get; }

    Color ActiveHighlightColor { get; }
    float ActiveNoteBlend { get; }
    float ActiveWhiteKeyBlend { get; }
    float ActiveBlackKeyBlend { get; }
}

public enum NoteShape
{
    Rectangular,
    DotBlock
}
