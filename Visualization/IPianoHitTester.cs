namespace Singularidi.Visualization;

/// <summary>
/// Maps a screen coordinate to a MIDI note number for a particular visualization's piano.
/// Each visualization engine owns its own implementation because piano geometry differs
/// between top-down (VerticalFall) and perspective (HorizontalCrawl) views.
/// </summary>
public interface IPianoHitTester
{
    /// <summary>
    /// Returns the MIDI note (0–127) at the given screen coordinate, or null if the point
    /// is not on a piano key. Width and height are the current control dimensions.
    /// </summary>
    int? HitTest(double screenX, double screenY, double width, double height);
}
