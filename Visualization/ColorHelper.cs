using Avalonia.Media;
using Singularidi.Midi;
using Singularidi.Themes;

namespace Singularidi.Visualization;

public static class ColorHelper
{
    public static Color LerpToColor(Color c, Color target, float t)
    {
        byte r = (byte)(c.R + (target.R - c.R) * t);
        byte g = (byte)(c.G + (target.G - c.G) * t);
        byte b = (byte)(c.B + (target.B - c.B) * t);
        return Color.FromRgb(r, g, b);
    }

    public static Color ResolveNoteColor(
        NoteEvent note,
        NoteColorMode colorMode,
        Color[] channelColors,
        Color[] trackColors,
        Dictionary<int, Color>? noteColorOverrides)
    {
        if (noteColorOverrides != null && noteColorOverrides.TryGetValue(note.NoteNumber, out var overrideColor))
            return overrideColor;

        if (colorMode == NoteColorMode.Track && trackColors.Length > 0)
            return trackColors[note.Track % trackColors.Length];

        return channelColors[note.Channel % 16];
    }

    public static Color ResolveActiveKeyColor(
        int noteNumber,
        int[] activeKeyChannel,
        int[] activeKeyTrack,
        NoteColorMode colorMode,
        Color[] channelColors,
        Color[] trackColors,
        Color highlightColor,
        float blendFactor)
    {
        int channel = activeKeyChannel[noteNumber];
        if (channel < 0) return default;

        Color keyBase;
        if (colorMode == NoteColorMode.Track && trackColors.Length > 0)
        {
            int track = activeKeyTrack[noteNumber];
            keyBase = trackColors[track >= 0 ? track % trackColors.Length : 0];
        }
        else
        {
            keyBase = channelColors[channel % 16];
        }

        return LerpToColor(keyBase, highlightColor, blendFactor);
    }

    public static Color Lighten(Color c, double amount)
    {
        byte r = (byte)(c.R + (255 - c.R) * amount);
        byte g = (byte)(c.G + (255 - c.G) * amount);
        byte b = (byte)(c.B + (255 - c.B) * amount);
        return Color.FromArgb(c.A, r, g, b);
    }

    public static Color Darken(Color c, double amount)
    {
        byte r = (byte)(c.R * (1 - amount));
        byte g = (byte)(c.G * (1 - amount));
        byte b = (byte)(c.B * (1 - amount));
        return Color.FromArgb(c.A, r, g, b);
    }
}
