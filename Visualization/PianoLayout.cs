namespace Singularidi.Visualization;

/// <summary>
/// Piano layout using equal-segment-within-groups positioning:
///   - Bottom: 75 white keys of equal width tiling the full control width
///   - Top: within CDE group (3 white, 2 black), 5 equal segments
///          within FGAB group (4 white, 3 black), 7 equal segments
///   - This produces natural piano geometry where boundary lines between white keys
///     are offset from black key centers (75% through F#/A#, 67% through C#/D#)
/// </summary>
public sealed class PianoLayout
{
    public static readonly bool[] IsBlackKey =
    [
        false, true, false, true, false, false, true, false, true, false, true, false
    ];

    public const double PianoHeightFraction = 0.15;
    public const double LookAheadSeconds = 4.0;
    public const double BlackKeyHeightFraction = 0.65;

    /// <summary>Center X of each note — for guide lines and note positioning.</summary>
    public readonly double[] XCenter = new double[128];
    /// <summary>Width of falling notes: WhiteKeyWidth for white keys, black key segment width for black.</summary>
    public readonly double[] NoteWidth = new double[128];

    /// <summary>Left edge of each white key's wide bottom section. -1 for black keys.</summary>
    public readonly double[] WhiteKeyBottomLeft = new double[128];
    /// <summary>Right edge of each white key's wide bottom section. -1 for black keys.</summary>
    public readonly double[] WhiteKeyBottomRight = new double[128];
    /// <summary>Left edge of each key's top section (narrow for white, full for black). -1 if unused.</summary>
    public readonly double[] KeyTopLeft = new double[128];
    /// <summary>Right edge of each key's top section. -1 if unused.</summary>
    public readonly double[] KeyTopRight = new double[128];

    /// <summary>Center of the narrow top portion for each key — used for UniformCentered guide lines.</summary>
    public readonly double[] GuideXUniform = new double[128];
    /// <summary>X positions of octave boundaries (between B and C). Used for Octave guide lines.</summary>
    public readonly List<double> OctaveBoundaryX = new();

    /// <summary>Width of the wide bottom portion of white keys (width / 75).</summary>
    public double WhiteKeyWidth { get; private set; }
    /// <summary>Black key width in the CDE group (3*WKW/5).</summary>
    public double BlackKeyWidthCDE { get; private set; }
    /// <summary>Black key width in the FGAB group (4*WKW/7).</summary>
    public double BlackKeyWidthFGAB { get; private set; }
    /// <summary>Average black key width (for engines that need a single value).</summary>
    public double BlackKeyWidth { get; private set; }
    /// <summary>Alias for backward compat.</summary>
    public double SlotWidth { get; private set; }

    private double _cachedWidth = -1;

    // Semitone-to-segment mapping within each group:
    // CDE group (semitones 0-4): C=0, C#=1, D=2, D#=3, E=4  → 5 segments
    // FGAB group (semitones 5-11): F=0, F#=1, G=2, G#=3, A=4, A#=5, B=6 → 7 segments
    private static readonly int[] SegmentIndex =
    [
        0, 1, 2, 3, 4,    // C, C#, D, D#, E
        0, 1, 2, 3, 4, 5, 6 // F, F#, G, G#, A, A#, B
    ];

    public void RebuildIfNeeded(double width)
    {
        if (Math.Abs(width - _cachedWidth) < 0.001) return;
        _cachedWidth = width;

        // Count white keys
        int totalWhiteKeys = 0;
        for (int n = 0; n < 128; n++)
            if (!IsBlackKey[n % 12]) totalWhiteKeys++;

        WhiteKeyWidth = width / totalWhiteKeys;
        BlackKeyWidthCDE = 3.0 * WhiteKeyWidth / 5.0;
        BlackKeyWidthFGAB = 4.0 * WhiteKeyWidth / 7.0;
        BlackKeyWidth = (BlackKeyWidthCDE + BlackKeyWidthFGAB) / 2.0;
        SlotWidth = BlackKeyWidth;

        // First pass: compute white key bottom positions
        int whiteIndex = 0;
        for (int note = 0; note < 128; note++)
        {
            WhiteKeyBottomLeft[note] = -1;
            WhiteKeyBottomRight[note] = -1;
            KeyTopLeft[note] = -1;
            KeyTopRight[note] = -1;

            if (!IsBlackKey[note % 12])
            {
                WhiteKeyBottomLeft[note] = whiteIndex * WhiteKeyWidth;
                WhiteKeyBottomRight[note] = (whiteIndex + 1) * WhiteKeyWidth;
                XCenter[note] = (whiteIndex + 0.5) * WhiteKeyWidth;
                NoteWidth[note] = WhiteKeyWidth;
                whiteIndex++;
            }
        }

        // Second pass: compute top positions using equal-segment-within-groups
        for (int note = 0; note < 128; note++)
        {
            int semitone = note % 12;
            bool isCDEgroup = semitone <= 4; // C, C#, D, D#, E
            int segIdx = SegmentIndex[semitone];

            // Find the octave's first white key index to determine group start position
            int octaveBase = note - semitone; // first note of this octave (C)

            double groupStartX; // left edge of the group in screen coordinates
            double segWidth;    // width of each segment

            if (isCDEgroup)
            {
                // CDE group starts at C's bottom left
                // C is the first white key in this octave
                groupStartX = GetWhiteKeyBottomLeft(octaveBase); // C's left edge
                segWidth = 3.0 * WhiteKeyWidth / 5.0;
            }
            else
            {
                // FGAB group starts at F's bottom left
                // F is semitone 5, so octaveBase + 5
                groupStartX = GetWhiteKeyBottomLeft(octaveBase + 5); // F's left edge
                segWidth = 4.0 * WhiteKeyWidth / 7.0;
            }

            if (groupStartX < 0)
            {
                // Edge case: partial octave at the end — use simple fallback
                if (!IsBlackKey[semitone])
                {
                    KeyTopLeft[note] = WhiteKeyBottomLeft[note];
                    KeyTopRight[note] = WhiteKeyBottomRight[note];
                }
                continue;
            }

            double topLeft = groupStartX + segIdx * segWidth;
            double topRight = groupStartX + (segIdx + 1) * segWidth;

            KeyTopLeft[note] = topLeft;
            KeyTopRight[note] = topRight;

            if (IsBlackKey[semitone])
            {
                XCenter[note] = (topLeft + topRight) / 2.0;
                NoteWidth[note] = segWidth;
            }
        }

        // Third pass: extend white keys to full bottom width on sides where the
        // adjacent black key doesn't exist (first/last notes of the keyboard)
        for (int note = 0; note < 128; note++)
        {
            if (IsBlackKey[note % 12]) continue;
            if (KeyTopLeft[note] < 0) continue;

            // If the expected black key to the left is beyond the keyboard, extend left
            bool hasBlackLeft = note > 0 && IsBlackKey[(note - 1) % 12];
            if (!hasBlackLeft)
                KeyTopLeft[note] = WhiteKeyBottomLeft[note];

            // If the expected black key to the right is beyond the keyboard, extend right
            bool hasBlackRight = note < 127 && IsBlackKey[(note + 1) % 12];
            if (!hasBlackRight)
                KeyTopRight[note] = WhiteKeyBottomRight[note];
        }

        // Fourth pass: compute uniform guide line positions (center of top portion)
        for (int note = 0; note < 128; note++)
        {
            if (KeyTopLeft[note] >= 0 && KeyTopRight[note] >= 0)
                GuideXUniform[note] = (KeyTopLeft[note] + KeyTopRight[note]) / 2.0;
            else
                GuideXUniform[note] = XCenter[note]; // fallback
        }

        // Fifth pass: compute octave boundary positions (between B and C)
        OctaveBoundaryX.Clear();
        for (int note = 0; note < 128; note++)
        {
            if (note % 12 != 0 || note == 0) continue; // C notes, skip the first one
            // Boundary is at C's bottom left edge
            if (WhiteKeyBottomLeft[note] >= 0)
                OctaveBoundaryX.Add(WhiteKeyBottomLeft[note]);
        }
    }

    private double GetWhiteKeyBottomLeft(int noteNumber)
    {
        if (noteNumber < 0 || noteNumber >= 128) return -1;
        return WhiteKeyBottomLeft[noteNumber];
    }
}
