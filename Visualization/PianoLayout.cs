namespace Singularidi.Visualization;

public sealed class PianoLayout
{
    public static readonly bool[] IsBlackKey =
    [
        false, true, false, true, false, false, true, false, true, false, true, false
    ];

    public static readonly double[] InOctaveOffset =
    [
        0.5, 1.0, 1.5, 2.0, 2.5,
        3.5, 4.0, 4.5, 5.0, 5.5, 6.0, 6.5
    ];

    public const int TotalWhiteKeys = 75;
    public const double PianoHeightFraction = 0.15;
    public const double LookAheadSeconds = 4.0;

    public readonly double[] XCenter = new double[128];
    public readonly double[] NoteWidth = new double[128];
    public double WhiteKeyWidth { get; private set; }
    public double BlackKeyWidth { get; private set; }

    private double _cachedWidth = -1;

    public void RebuildIfNeeded(double width)
    {
        if (Math.Abs(width - _cachedWidth) < 0.001) return;
        _cachedWidth = width;
        WhiteKeyWidth = width / TotalWhiteKeys;
        BlackKeyWidth = WhiteKeyWidth * 0.60;

        for (int note = 0; note < 128; note++)
        {
            int octave = note / 12;
            int semitone = note % 12;
            XCenter[note] = octave * 7 * WhiteKeyWidth + InOctaveOffset[semitone] * WhiteKeyWidth;
            NoteWidth[note] = IsBlackKey[semitone] ? BlackKeyWidth : WhiteKeyWidth;
        }
    }
}
